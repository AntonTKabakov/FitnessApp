using System.Text;


namespace FitnessApp.Service;
public class JwtSettings
{
    private const int MinimumSecretSizeInBytes = 32;

    public string AccessSecret { get; init; } = string.Empty;

    public static JwtSettings FromEnvironment()
    {
        var accessSecret = Environment.GetEnvironmentVariable("JWT_ACCESS_SECRET");

        if (string.IsNullOrWhiteSpace(accessSecret))
        {
            throw new InvalidOperationException("JWT_ACCESS_SECRET must be set.");
        }

        if (Encoding.UTF8.GetByteCount(accessSecret) < MinimumSecretSizeInBytes)
        {
            throw new InvalidOperationException(
                "JWT_ACCESS_SECRET must be at least 32 bytes for HS256 token signing.");
        }

        return new JwtSettings
        {
            AccessSecret = accessSecret
        };
    }

    public static JwtSettings FromConfiguration(
        IConfiguration configuration)
    {
        var accessSecret = configuration["Jwt:AccessSecret"];

        if (string.IsNullOrWhiteSpace(accessSecret))
            throw new InvalidOperationException(
                "Jwt:AccessSecret must be configured.");

        if (Encoding.UTF8.GetByteCount(accessSecret) < 32)
            throw new InvalidOperationException(
                "JWT secret must be at least 32 bytes.");

        return new JwtSettings
        {
            AccessSecret = accessSecret
        };
    }
}
