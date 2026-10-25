
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

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

// Authentication services
var jwtSettings = JwtSettings.FromConfiguration(
    builder.Configuration);

var authCookieSettings = AuthCookieSettings.FromConfiguration(
    builder.Configuration,
    builder.Environment);

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(authCookieSettings);
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

                NameClaimType = ClaimTypes.NameIdentifier
            };

        options.Events = new JwtBearerEvents
        {
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

builder.Services.AddAuthorization();

var app = builder.Build();

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

app.MapStaticAssets();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
