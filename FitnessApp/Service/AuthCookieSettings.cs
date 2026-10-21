using Microsoft.AspNetCore.Http;

namespace FitnessApp.Service;

public class AuthCookieSettings
{
    public bool Secure { get; init; }

    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;

    public string? Domain { get; init; }

    public static AuthCookieSettings FromEnvironment(IHostEnvironment environment)
    {
        var secure = ParseBoolean(
            Environment.GetEnvironmentVariable("AUTH_COOKIE_SECURE"),
            defaultValue: !environment.IsDevelopment());
        var sameSite = ParseSameSiteMode(Environment.GetEnvironmentVariable("AUTH_COOKIE_SAME_SITE"));
        var domain = NormalizeOptionalValue(Environment.GetEnvironmentVariable("AUTH_COOKIE_DOMAIN"));

        if (sameSite == SameSiteMode.None && !secure)
        {
            throw new InvalidOperationException(
                "AUTH_COOKIE_SECURE must be true when AUTH_COOKIE_SAME_SITE is set to None.");
        }

        return new AuthCookieSettings
        {
            Secure = secure,
            SameSite = sameSite,
            Domain = domain
        };
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsedValue)
            ? parsedValue
            : defaultValue;
    }

    private static SameSiteMode ParseSameSiteMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return SameSiteMode.Lax;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "strict" => SameSiteMode.Strict,
            "lax" => SameSiteMode.Lax,
            "none" => SameSiteMode.None,
            _ => throw new InvalidOperationException(
                "AUTH_COOKIE_SAME_SITE must be one of Strict, Lax, or None.")
        };
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var normalizedValue = value?.Trim();
        return string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue;
    }
}
