using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FitnessApp.Service;

public class AuthCookieSettings
{
    public bool Secure { get; init; }

    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;

    public string? Domain { get; init; }

    public static AuthCookieSettings FromConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection("AuthCookie");

        var secure = section.GetValue<bool?>("Secure")
                     ?? !environment.IsDevelopment();

        var sameSite = ParseSameSiteMode(section["SameSite"]);

        var domain = NormalizeOptionalValue(section["Domain"]);

        if (sameSite == SameSiteMode.None && !secure)
        {
            throw new InvalidOperationException(
                "AuthCookie:Secure must be true when AuthCookie:SameSite is None.");
        }

        return new AuthCookieSettings
        {
            Secure = secure,
            SameSite = sameSite,
            Domain = domain
        };
    }

    private static SameSiteMode ParseSameSiteMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SameSiteMode.Lax;

        return value.Trim().ToLowerInvariant() switch
        {
            "strict" => SameSiteMode.Strict,
            "lax" => SameSiteMode.Lax,
            "none" => SameSiteMode.None,
            _ => throw new InvalidOperationException(
                "AuthCookie:SameSite must be Strict, Lax, or None.")
        };
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        var normalized = value?.Trim();

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}