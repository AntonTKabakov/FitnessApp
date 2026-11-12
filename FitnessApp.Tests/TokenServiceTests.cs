using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FitnessApp.Models;
using FitnessApp.Service;
using Microsoft.IdentityModel.Tokens;

namespace FitnessApp.Tests;

public sealed class TokenServiceTests
{
    internal const string SigningSecret = "unit-tests-only-signing-key-at-least-32-bytes";

    [Fact]
    public void Generated_access_token_has_valid_signature_user_role_and_fifteen_minute_expiry()
    {
        var service = new TokenService(new JwtSettings { AccessSecret = SigningSecret });
        var before = DateTime.UtcNow;

        var encoded = service.GenerateAccessToken("42", UserRoles.Admin);

        var principal = new JwtSecurityTokenHandler().ValidateToken(encoded, ValidationParameters(SigningSecret), out var validated);
        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole(UserRoles.Admin));
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.IsType<JwtSecurityToken>(validated).Header.Alg);
        // JWT stores seconds, so allow only its subsecond truncation rather than arbitrary delays.
        Assert.InRange(validated.ValidTo, before.AddMinutes(15).AddSeconds(-1), DateTime.UtcNow.AddMinutes(15));
    }

    [Fact]
    public void Generated_access_token_is_rejected_when_validated_with_another_signing_key()
    {
        var service = new TokenService(new JwtSettings { AccessSecret = SigningSecret });
        var encoded = service.GenerateAccessToken("42");

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler()
            .ValidateToken(encoded, ValidationParameters("different-unit-test-signing-key-with-32-bytes"), out _));
    }

    private static TokenValidationParameters ValidationParameters(string secret) => new()
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero
    };
}
