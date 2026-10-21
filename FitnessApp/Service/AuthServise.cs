using FitnessApp.Data;
using FitnessApp.DTO;
using FitnessApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace FitnessApp.Service;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<User> _userHasher;
    private readonly TokenService _tokenService;
    private readonly AuthCookieSettings _authCookieSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthService(
        AppDbContext db,
        PasswordHasher<User> userHasher,
        TokenService tokenService,
        AuthCookieSettings authCookieSettings,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _userHasher = userHasher;
        _tokenService = tokenService;
        _authCookieSettings = authCookieSettings;
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CheckRegisterRequest(RegisterRequest req)
    {
        var email = (req.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
            return "Email is required.";

        if (!new EmailAddressAttribute().IsValid(email))
            return "Email format is invalid.";

        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return "Password must be at least 8 characters.";

        if (req.Username?.Trim().Length > 50)
            return "Username must be 50 characters or fewer.";

        return null;
    }

    private async Task IssueTokens(
        User user,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");

        string refreshJwt;
        string hashedRefreshToken;
        bool exists;

        do
        {
            refreshJwt = _tokenService.GenerateRefreshToken();
            hashedRefreshToken = TokenHasher.Hash(refreshJwt);

            exists = await _db.RefreshToken
                .AnyAsync(
                    rt => rt.Token == hashedRefreshToken,
                    cancellationToken);
        }
        while (exists);

        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(7);

        var newRefreshToken = new RefreshToken
        {
            User = user,
            UserId = user.Id,
            ExpiresAt = refreshTokenExpiresAt,
            CreatedAt = DateTime.UtcNow,
            Token = hashedRefreshToken
        };

        _db.RefreshToken.Add(newRefreshToken);
        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            user.Id.ToString());

        var accessTokenExpiresAt = DateTime.UtcNow.AddMinutes(15);

        httpContext.Response.Cookies.Append(
            "access_token",
            accessToken,
            BuildCookieOptions(accessTokenExpiresAt));

        httpContext.Response.Cookies.Append(
            "refresh_token",
            refreshJwt,
            BuildCookieOptions(refreshTokenExpiresAt));
    }

    private CookieOptions BuildCookieOptions(DateTime expiresAt)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");

        var secure = _authCookieSettings.Secure
            && httpContext.Request.IsHttps;

        return new CookieOptions
        {
            Domain = ResolveCookieDomain(),
            Expires = new DateTimeOffset(
                DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)),
            HttpOnly = true,
            IsEssential = true,
            Path = "/",
            SameSite = ResolveSameSite(secure),
            Secure = secure
        };
    }

    private SameSiteMode ResolveSameSite(bool secure)
    {
        return !secure && _authCookieSettings.SameSite == SameSiteMode.None
            ? SameSiteMode.Lax
            : _authCookieSettings.SameSite;
    }

    private string? ResolveCookieDomain()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");

        var configuredDomain = _authCookieSettings.Domain;
        var requestHost = httpContext.Request.Host.Host;

        if (string.IsNullOrWhiteSpace(configuredDomain) ||
            string.IsNullOrWhiteSpace(requestHost) ||
            requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(requestHost, out _))
        {
            return null;
        }

        var normalizedDomain = configuredDomain.Trim().TrimStart('.');

        if (string.IsNullOrWhiteSpace(normalizedDomain))
            return null;

        return requestHost.Equals(
                   normalizedDomain,
                   StringComparison.OrdinalIgnoreCase) ||
               requestHost.EndsWith(
                   $".{normalizedDomain}",
                   StringComparison.OrdinalIgnoreCase)
            ? normalizedDomain
            : null;
    }
}