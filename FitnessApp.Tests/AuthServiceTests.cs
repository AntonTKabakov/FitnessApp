using System.IdentityModel.Tokens.Jwt;
using FitnessApp.DTO;
using FitnessApp.Models;
using FitnessApp.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace FitnessApp.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly ServiceTestDatabase database = new();
    private readonly Mock<PasswordHasher<User>> hasher = new(MockBehavior.Strict, Options.Create(new PasswordHasherOptions()));
    private readonly Mock<IHttpContextAccessor> accessor = new(MockBehavior.Strict);
    private readonly DefaultHttpContext httpContext = new();
    private readonly AuthService service;

    public AuthServiceTests()
    {
        httpContext.Request.Host = new HostString("localhost");
        accessor.SetupGet(x => x.HttpContext).Returns(httpContext);
        service = new AuthService(database.Db, hasher.Object,
            new TokenService(new JwtSettings { AccessSecret = TokenServiceTests.SigningSecret }),
            new AuthCookieSettings { Secure = true, SameSite = SameSiteMode.Lax }, accessor.Object);
    }

    [Fact]
    public async Task Login_with_valid_password_normalizes_email_and_issues_protected_cookies_and_hashed_refresh_token()
    {
        var user = AddUser();
        hasher.Setup(x => x.VerifyHashedPassword(user, "stored-hash", "correct-password"))
            .Returns(PasswordVerificationResult.Success);

        var result = await service.Login(new LoginRequest("  MEMBER@EXAMPLE.TEST ", "correct-password"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Id);
        Assert.Equal(user.Email, result.Email);
        Assert.Equal(user.Fullname, result.Username);
        var refresh = Assert.Single(database.Db.RefreshTokens);
        Assert.Equal(user.Id, refresh.UserId);
        Assert.Equal(TokenHasher.Hash(CookieValue("refresh_token")), refresh.Token);
        Assert.NotEqual(CookieValue("refresh_token"), refresh.Token);
        Assert.Equal(TimeSpan.FromDays(7), refresh.ExpiresAt - refresh.CreatedAt);
        Assert.Equal(user.Id.ToString(), new JwtSecurityTokenHandler().ReadJwtToken(CookieValue("access_token")).Subject);
        foreach (var cookie in httpContext.Response.Headers.SetCookie)
        {
            Assert.NotNull(cookie);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        }
        hasher.Verify(x => x.VerifyHashedPassword(user, "stored-hash", "correct-password"), Times.Once);
        hasher.VerifyNoOtherCalls();
        accessor.VerifyGet(x => x.HttpContext, Times.AtLeastOnce);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_generic_failure_and_issues_no_tokens()
    {
        var user = AddUser();
        hasher.Setup(x => x.VerifyHashedPassword(user, "stored-hash", "wrong-password"))
            .Returns(PasswordVerificationResult.Failed);

        var result = await service.Login(new LoginRequest(user.Email, "wrong-password"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid email or password.", result.Error);
        Assert.Empty(database.Db.RefreshTokens);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
        hasher.Verify(x => x.VerifyHashedPassword(user, "stored-hash", "wrong-password"), Times.Once);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_same_generic_failure_without_verifying_password()
    {
        var result = await service.Login(new LoginRequest("unknown@example.test", "wrong-password"), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Invalid email or password.", result.Error);
        Assert.Null(result.Id);
        Assert.Empty(database.Db.RefreshTokens);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
        hasher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Login_when_password_needs_rehash_persists_the_new_hash()
    {
        var user = AddUser();
        hasher.Setup(x => x.VerifyHashedPassword(user, "stored-hash", "correct-password"))
            .Returns(PasswordVerificationResult.SuccessRehashNeeded);
        hasher.Setup(x => x.HashPassword(user, "correct-password")).Returns("upgraded-hash");

        var result = await service.Login(new LoginRequest(user.Email, "correct-password"), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        database.Db.ChangeTracker.Clear();
        Assert.Equal("upgraded-hash", (await database.Db.Users.SingleAsync(TestContext.Current.CancellationToken)).Password);
        hasher.Verify(x => x.HashPassword(user, "correct-password"), Times.Once);
    }

    [Fact]
    public async Task Registration_normalizes_identity_hashes_password_and_preserves_default_member_role()
    {
        hasher.Setup(x => x.HashPassword(It.Is<User>(u => u.Email == "new@example.test"), "register-password"))
            .Returns("new-password-hash");
        var request = new RegisterRequest
        {
            Email = " NEW@EXAMPLE.TEST ", Password = "register-password", Username = " New member ",
            Dateofbirth = new DateOnly(1990, 1, 1)
        };

        var result = await service.CreateUser(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(database.Db.Users);
        Assert.Equal("new@example.test", user.Email);
        Assert.Equal("New member", user.Fullname);
        Assert.Equal("new-password-hash", user.Password);
        Assert.Equal(UserRoles.User, user.Role);
        Assert.Equal(user.Id, result.Id);
        Assert.Single(database.Db.RefreshTokens);
        hasher.Verify(x => x.HashPassword(It.Is<User>(u => u.Id == user.Id), "register-password"), Times.Once);
    }

    [Fact]
    public async Task Registration_with_existing_normalized_email_creates_no_user_or_tokens()
    {
        AddUser();

        var result = await service.CreateUser(new RegisterRequest
        {
            Email = " MEMBER@EXAMPLE.TEST ", Password = "register-password", Dateofbirth = new DateOnly(1990, 1, 1)
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Email already exists.", result.Error);
        Assert.Single(database.Db.Users);
        Assert.Empty(database.Db.RefreshTokens);
        Assert.Equal(0, httpContext.Response.Headers.SetCookie.Count);
        hasher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("invalid-email", "valid-password", "Email format is invalid.")]
    [InlineData("member@example.test", "short", "Password must be at least 8 characters.")]
    public async Task Registration_with_invalid_input_fails_before_hashing_or_saving(string email, string password, string expectedError)
    {
        var result = await service.CreateUser(new RegisterRequest
        {
            Email = email, Password = password, Dateofbirth = new DateOnly(1990, 1, 1)
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
        Assert.Empty(database.Db.Users);
        Assert.Empty(database.Db.RefreshTokens);
        hasher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Refresh_with_valid_token_rotates_it_and_rejects_reuse()
    {
        var user = AddUser();
        var previousToken = AddRefreshToken(user, DateTime.UtcNow.AddDays(1));
        httpContext.Request.Headers.Cookie = "refresh_token=known-refresh-token";

        var refreshed = await service.Refresh(TestContext.Current.CancellationToken);
        var replacement = Assert.Single(database.Db.RefreshTokens);
        var replacementHash = replacement.Token;
        var replayed = await service.Refresh(TestContext.Current.CancellationToken);

        Assert.True(refreshed.IsSuccess);
        Assert.Equal(user.Id, refreshed.Id);
        Assert.NotEqual(previousToken.Token, replacementHash);
        Assert.False(replayed.IsSuccess);
        Assert.Equal("Invalid refresh token.", replayed.Error);
        Assert.Equal(replacementHash, Assert.Single(database.Db.RefreshTokens).Token);
    }

    [Fact]
    public async Task Refresh_with_expired_token_removes_it_and_clears_authentication_cookies()
    {
        var user = AddUser();
        AddRefreshToken(user, DateTime.UtcNow.AddDays(-1));
        httpContext.Request.Headers.Cookie = "refresh_token=known-refresh-token";

        var result = await service.Refresh(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Refresh token has expired.", result.Error);
        Assert.Empty(database.Db.RefreshTokens);
        AssertCookiesCleared();
    }

    [Fact]
    public async Task Refresh_without_token_fails_and_clears_authentication_cookies()
    {
        var result = await service.Refresh(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Refresh token is missing.", result.Error);
        Assert.Empty(database.Db.RefreshTokens);
        AssertCookiesCleared();
    }

    [Fact]
    public async Task Logout_revokes_only_the_current_refresh_token_and_clears_cookies()
    {
        var user = AddUser();
        AddRefreshToken(user, DateTime.UtcNow.AddDays(1));
        var otherSessionHash = TokenHasher.Hash("other-session");
        database.Db.RefreshTokens.Add(new RefreshToken
        {
            User = user, Token = otherSessionHash, ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        httpContext.Request.Headers.Cookie = "refresh_token=known-refresh-token";

        await service.Logout(TestContext.Current.CancellationToken);

        Assert.Equal(otherSessionHash, Assert.Single(database.Db.RefreshTokens).Token);
        AssertCookiesCleared();
    }

    private User AddUser()
    {
        var user = new User { Email = "member@example.test", Fullname = "Member", Password = "stored-hash" };
        database.Db.Users.Add(user);
        database.Db.SaveChanges();
        return user;
    }

    private RefreshToken AddRefreshToken(User user, DateTime expires)
    {
        var token = new RefreshToken { User = user, Token = TokenHasher.Hash("known-refresh-token"), ExpiresAt = expires };
        database.Db.RefreshTokens.Add(token);
        database.Db.SaveChanges();
        return token;
    }

    private string CookieValue(string name)
    {
        var cookie = Assert.Single(httpContext.Response.Headers.SetCookie, x => x!.StartsWith(name + "=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(cookie![(name.Length + 1)..].Split(';')[0]);
    }

    private void AssertCookiesCleared()
    {
        Assert.Contains(httpContext.Response.Headers.SetCookie, x => x!.StartsWith("access_token=;", StringComparison.Ordinal));
        Assert.Contains(httpContext.Response.Headers.SetCookie, x => x!.StartsWith("refresh_token=;", StringComparison.Ordinal));
    }

    public void Dispose() => database.Dispose();
}
