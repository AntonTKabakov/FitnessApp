using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace FitnessApp.Tests;

// This fixture never connects to the application's configured PostgreSQL database.
public sealed class FitnessAppFactory : WebApplicationFactory<Program>
{
    public const string Password = "Picture-test-password!42";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "fitness-picture-tests-" + Guid.NewGuid().ToString("N"));
    public string Pictures => Path.Combine(Root, "pictures");
    public FailureSwitches Failures { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(Root);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused_test_database;Username=unused",
            ["Jwt:AccessSecret"] = "isolated-profile-picture-tests-secret-at-least-32-bytes",
            ["AuthCookie:Secure"] = "true",
            ["AuthCookie:SameSite"] = "Lax",
            ["AdminBootstrap:Email"] = "",
            ["FileStorage:ProfilePicturesPath"] = Pictures,
            ["FileStorage:MaxProfilePictureSizeMB"] = "5",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(Root, "tests.db"))
                .AddInterceptors(new ProfileWriteFailureInterceptor(Failures)));
            services.RemoveAll<IProfilePictureStorageService>();
            services.AddSingleton<IProfilePictureStorageService>(provider => new FaultingPictureStorage(
                ActivatorUtilities.CreateInstance<LocalProfilePictureStorageService>(provider), Failures));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Minimal hosting reads these before ConfigureWebHost's app-configuration callback.
        // Host configuration is passed to the application's entry point before it runs.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused_test_database;Username=unused",
            ["Jwt:AccessSecret"] = "isolated-profile-picture-tests-secret-at-least-32-bytes",
            ["AuthCookie:Secure"] = "true",
            ["AuthCookie:SameSite"] = "Lax",
            ["AdminBootstrap:Email"] = "",
            ["FileStorage:ProfilePicturesPath"] = Pictures
        }));
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return host;
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    });

    public async Task<TestAccount> RegisterAsync(string role = UserRoles.User)
    {
        var client = NewClient();
        var email = Guid.NewGuid().ToString("N") + "@pictures.test";
        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email, password = Password, username = "Picture member", dateofbirth = "1990-01-01", role = "Admin"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = result.RootElement.GetProperty("id").GetInt32();
        // Registration must ignore role overposting. Admin access is granted explicitly in this isolated fixture.
        await WithDatabaseAsync(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == id);
            Assert.Equal(UserRoles.User, user.Role);
            if (role != UserRoles.User)
            {
                user.Role = role;
                await db.SaveChangesAsync();
            }
        });
        return new TestAccount(client, id, email);
    }

    public async Task<string> AntiforgeryAsync(HttpClient client)
    {
        var page = await client.GetAsync("/Member/Index");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return WebUtility.HtmlDecode(token);
    }

    public async Task WithDatabaseAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<string?> PictureKeyAsync(int userId)
    {
        string? key = null;
        await WithDatabaseAsync(async db => key = await db.Users.Where(x => x.Id == userId)
            .Select(x => x.ProfilePicturePath).SingleAsync());
        return key;
    }

    public async Task<HttpClient> BearerClientAsync(int userId)
    {
        var client = NewClient();
        using var scope = Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<TokenService>().GenerateAccessToken(userId.ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        return await Task.FromResult(client);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        // Only the unique, absolute directory created by this fixture can be removed.
        var expected = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Root);
        if (target.StartsWith(expected, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(target).StartsWith("fitness-picture-tests-", StringComparison.Ordinal) && Directory.Exists(target))
            Directory.Delete(target, recursive: true);
    }
}

public sealed record TestAccount(HttpClient Client, int Id, string Email) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

public sealed class FailureSwitches
{
    public int FailNextDatabaseWrite;
    public int FailNextSave;
    public int FailNextDelete;
    public int FailNextRead;
}

internal sealed class ProfileWriteFailureInterceptor(FailureSwitches failures) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var changesPicture = eventData.Context!.ChangeTracker.Entries<User>()
            .Any(x => x.State == EntityState.Modified && x.Property(u => u.ProfilePicturePath).IsModified);
        if (changesPicture && Interlocked.Exchange(ref failures.FailNextDatabaseWrite, 0) == 1)
            throw new DbUpdateException("Simulated profile database write failure.");
        return ValueTask.FromResult(result);
    }
}

internal sealed class FaultingPictureStorage(IProfilePictureStorageService inner, FailureSwitches failures) : IProfilePictureStorageService
{
    public Task<string> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) =>
        Interlocked.Exchange(ref failures.FailNextSave, 0) == 1
            ? throw new ProfilePictureException(503, "Picture storage is temporarily unavailable.")
            : inner.SaveAsync(file, cancellationToken);

    public Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
        Interlocked.Exchange(ref failures.FailNextRead, 0) == 1
            ? throw new ProfilePictureException(503, "Picture storage is temporarily unavailable.")
            : inner.OpenReadAsync(path, cancellationToken);

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        Interlocked.Exchange(ref failures.FailNextDelete, 0) == 1
            ? throw new ProfilePictureException(503, "Picture storage is temporarily unavailable.")
            : inner.DeleteAsync(path, cancellationToken);
}
