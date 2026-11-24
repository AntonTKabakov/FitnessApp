using System.Text;
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Repositories;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
// Avoid the Windows Event Log provider in local development; it can terminate the host
// when the process cannot write to the machine event log.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Configure the data source.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure the CORS policy for frontend integration
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173", // Vite dev server
            "http://localhost:8080") // Production URL (example)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add CORS middleware to the request pipeline.
builder.Services.AddCors();

var jwt = JwtSettings.FromConfiguration(builder.Configuration);
var cookieSettings = AuthCookieSettings.FromConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton(cookieSettings);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<PasswordHasher<User>>();
builder.Services.AddScoped<AccountRoleService>();
builder.Services.AddScoped<ReceptionQueryService>();
builder.Services.AddScoped<UserProfileService>();
builder.Services.Configure<ProfilePictureStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddSingleton<IProfilePictureStorageService, LocalProfilePictureStorageService>();
builder.Services.AddScoped<ICustomerPassRepository, CustomerPassRepository>();
builder.Services.AddScoped<ICustomerPassService, CustomerPassService>();
builder.Services.AddScoped<ICheckinRepository, CheckinRepository>();
builder.Services.AddScoped<ICheckinService, CheckinService>();
builder.Services.AddScoped<ITrainerService, TrainerService>();
builder.Services.AddScoped<IGympassService, GympassService>();
builder.Services.AddScoped<IStatsService, StatsService>();
builder.Services.AddScoped<ReportExportService>();
builder.Services.AddScoped<IReportExportStrategy, ExcelReportService>();
builder.Services.AddScoped<IReportExportStrategy, PdfReportService>();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.AccessSecret)),
            ValidateLifetime = true,
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var identity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                var idClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (identity == null || !int.TryParse(idClaim, out var id))
                {
                    context.Fail("Invalid account.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var account = await db.Users.AsNoTracking().Where(x => x.Id == id)
                    .Select(x => new { x.Role }).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (account == null || !UserRoles.IsValid(account.Role))
                {
                    context.Fail("Account no longer exists.");
                    return;
                }

                foreach (var claim in identity.FindAll(System.Security.Claims.ClaimTypes.Role).ToArray())
                    identity.RemoveClaim(claim);
                identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, account.Role));
            },
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue("access_token", out var token)) context.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapStaticAssets().AllowAnonymous();

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// REST endpoints declare their complete paths with attribute routing.
app.MapControllers();

app.Run();

public partial class Program { }
