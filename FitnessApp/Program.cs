
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new ResponseCacheAttribute { NoStore = true, Location = ResponseCacheLocation.None }));

// Database
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not configured.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Existing services
builder.Services.AddScoped<ITrainerService, TrainerService>();
builder.Services.AddScoped<IGympassService, GympassService>();
builder.Services.AddScoped<ICheckinService, CheckinService>();
builder.Services.AddScoped<ReceptionQueryService>();
builder.Services.AddScoped<ICustomerPassService, CustomerPassService>();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AccountRoleService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.AddSingleton<IProfilePictureStorageService, LocalProfilePictureStorageService>();
builder.Services.AddOptions<ProfilePictureStorageOptions>()
    .Bind(builder.Configuration.GetSection(ProfilePictureStorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Authentication services
var jwtSettings = JwtSettings.FromConfiguration(
    builder.Configuration);

var authCookieSettings = AuthCookieSettings.FromConfiguration(
    builder.Configuration,
    builder.Environment);

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(authCookieSettings);

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();

builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddHttpContextAccessor();

// JWT Authentication using cookies
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,

                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.AccessSecret)),

                // Match the current TokenService implementation
                ValidateIssuer = false,
                ValidateAudience = false,

                ValidateLifetime = true,

                RequireExpirationTime = true,
                RequireSignedTokens = true,

                ClockSkew = TimeSpan.Zero,

                NameClaimType = ClaimTypes.NameIdentifier,
                RoleClaimType = ClaimTypes.Role
            };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                if (!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                {
                    context.Fail("Invalid account.");
                    return;
                }
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var account = await db.Users.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.Role, x.Fullname }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (identity == null || account == null || !UserRoles.IsValid(account.Role))
                {
                    context.Fail("Account no longer exists.");
                    return;
                }
                // Read the current role so demotion takes effect even for an unexpired JWT.
                foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList()) identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(ClaimTypes.Role, account.Role));
                identity.AddClaim(new Claim(ClaimTypes.GivenName, account.Fullname));
            },
            OnChallenge = context =>
            {
                var action = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                if (action != null && typeof(Controller).IsAssignableFrom(action.ControllerTypeInfo) &&
                    (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
                {
                    context.HandleResponse();
                    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect("/Account/Login" + QueryString.Create("returnUrl", returnUrl));
                }
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                var action = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                if (action != null && typeof(Controller).IsAssignableFrom(action.ControllerTypeInfo))
                    context.Response.Redirect("/Account/AccessDenied");
                return Task.CompletedTask;
            },
            OnMessageReceived = context =>
            {
                // Get JWT from HTTP-only cookie
                if (context.Request.Cookies.TryGetValue(
                        "access_token", out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

var app = builder.Build();

// Validate the private storage location at startup, before accepting requests.
_ = app.Services.GetRequiredService<IProfilePictureStorageService>();

// Explicit, one-time setup. Never promote the first registrant or accept a role from registration.
var adminEmail = builder.Configuration["AdminBootstrap:Email"]?.Trim().ToLowerInvariant();
if (!string.IsNullOrWhiteSpace(adminEmail))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!await db.Users.AnyAsync(x => x.Role == UserRoles.Admin))
    {
        var account = await db.Users.SingleOrDefaultAsync(x => x.Email == adminEmail);
        if (account == null)
        {
            var password = builder.Configuration["AdminBootstrap:Password"];
            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("AdminBootstrap:Password is required to create the first administrator.");
            account = new User
            {
                Email = adminEmail,
                Fullname = builder.Configuration["AdminBootstrap:Name"]?.Trim() ?? "Admin"
            };
            account.Password = scope.ServiceProvider.GetRequiredService<PasswordHasher<User>>().HashPassword(account, password);
            db.Users.Add(account);
        }
        account.Role = UserRoles.Admin;
        await db.SaveChangesAsync();
    }
}
if (builder.Configuration.GetValue<bool>("bootstrap-admin-only"))
{
    await app.DisposeAsync();
    return;
}

// HTTP pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

// Authenticate before authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program { }
