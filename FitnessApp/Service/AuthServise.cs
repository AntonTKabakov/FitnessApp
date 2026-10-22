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
        var email = (req.Email ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(email))
            return "Email is required.";

        if (!new EmailAddressAttribute().IsValid(email))
            return "Email format is invalid.";

        if (string.IsNullOrWhiteSpace(req.Password) ||
            req.Password.Length < 8)
            return "Password must be at least 8 characters.";

        if (req.Username?.Trim().Length > 50)
            return "Username must be 50 characters or fewer.";

        if (req.Dateofbirth == default)
            return "Date of birth is required.";

        if (req.Dateofbirth > DateOnly.FromDateTime(DateTime.UtcNow))
            return "Date of birth cannot be in the future.";

        return null;
    }

    public async Task<AuthResult> CreateUser(
        RegisterRequest req,
        CancellationToken cancellationToken = default)
    {
        var validationError = CheckRegisterRequest(req);

        if (validationError is not null)
            return AuthResult.Failure(validationError);

        var email = req.Email.Trim().ToLowerInvariant();

        var exists = await _db.Customers
            .AnyAsync(u => u.Email == email, cancellationToken);

        if (exists)
            return AuthResult.Failure("Email already exists.");

        var user = new User
        {
            Email = email,
            Fullname = string.IsNullOrWhiteSpace(req.Username)
                ? string.Empty
                : req.Username.Trim(),
            Dateofbirth = req.Dateofbirth
        };

        user.Password = _userHasher.HashPassword(
            user,
            req.Password);

        _db.Customers.Add(user);

        await _db.SaveChangesAsync(cancellationToken);

        await IssueTokens(user, cancellationToken);

        return AuthResult.Success(
            user.Id,
            user.Email,
            user.Fullname);
    }

    public async Task<AuthResult> Login(
        LoginRequest req,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(req.Email) ||
            string.IsNullOrWhiteSpace(req.Password))
        {
            return AuthResult.Failure("Email and password are required.");
        }

        var email = req.Email.Trim().ToLowerInvariant();

        var user = await _db.Customers
            .SingleOrDefaultAsync(
                u => u.Email == email,
                cancellationToken);

        if (user is null || string.IsNullOrWhiteSpace(user.Password))
            return AuthResult.Failure("Invalid email or password.");

        var verificationResult = _userHasher.VerifyHashedPassword(
            user,
            user.Password,
            req.Password);

        if (verificationResult == PasswordVerificationResult.Failed)
            return AuthResult.Failure("Invalid email or password.");

        if (verificationResult ==
            PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.Password = _userHasher.HashPassword(
                user,
                req.Password);
        }


        await IssueTokens(user, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return AuthResult.Success(
            user.Id,
            user.Email,
            user.Fullname);
    }

    private async Task IssueTokens(
        User user,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No active HTTP context.");

        string refreshJwt;
        string hashedRefreshToken;
        bool exists;

        do
        {
            refreshJwt = _tokenService.GenerateRefreshToken();
            hashedRefreshToken = TokenHasher.Hash(refreshJwt);

            exists = await _db.RefreshTokens.AnyAsync(
                rt => rt.Token == hashedRefreshToken,
                cancellationToken);
        }
        while (exists);

        var now = DateTime.UtcNow;
        var refreshTokenExpiresAt = now.AddDays(7);

        var refreshToken = new RefreshToken
        {
            User = user,
            UserId = user.Id,
            Token = hashedRefreshToken,
            CreatedAt = now,
            ExpiresAt = refreshTokenExpiresAt
        };

        _db.RefreshTokens.Add(refreshToken);

        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            user.Id.ToString());

        var accessTokenExpiresAt = now.AddMinutes(15);

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
            ?? throw new InvalidOperationException(
                "No active HTTP context.");

        var secure = _authCookieSettings.Secure;

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
        return !secure &&
               _authCookieSettings.SameSite == SameSiteMode.None
            ? SameSiteMode.Lax
            : _authCookieSettings.SameSite;
    }

    private string? ResolveCookieDomain()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No active HTTP context.");

        var configuredDomain = _authCookieSettings.Domain;
        var requestHost = httpContext.Request.Host.Host;

        if (string.IsNullOrWhiteSpace(configuredDomain) ||
            string.IsNullOrWhiteSpace(requestHost) ||
            requestHost.Equals(
                "localhost",
                StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(requestHost, out _))
        {
            return null;
        }

        var normalizedDomain = configuredDomain
            .Trim()
            .TrimStart('.');

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