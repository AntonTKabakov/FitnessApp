using System.Security.Claims;
using System.Text.Json;
using FitnessApp.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FitnessApp.Tests;

public sealed class PersistentErrorLoggingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "fitness-error-logs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Error_entry_contains_exception_request_user_and_correlation_metadata()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = "/club";
        context.Request.Path = "/api/checkins";
        context.Request.QueryString = new QueryString("?password=query-secret");
        context.Request.Method = "POST";
        context.TraceIdentifier = "test-correlation";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], "test"));
        using var provider = CreateProvider(context);
        var before = DateTimeOffset.UtcNow;

        provider.CreateLogger("FitnessApp.Service.CheckinService").LogError(CreateException("A diagnostic failure"), "Check-in failed.");

        var entry = ReadEntries().Single();
        Assert.InRange(entry.GetProperty("Timestamp").GetDateTimeOffset(), before, DateTimeOffset.UtcNow);
        Assert.Equal("System.InvalidOperationException", entry.GetProperty("ExceptionType").GetString());
        Assert.Equal("A diagnostic failure", entry.GetProperty("ExceptionMessage").GetString());
        Assert.Contains(nameof(CreateException), entry.GetProperty("StackTrace").GetString());
        Assert.Equal("/club/api/checkins", entry.GetProperty("RequestPath").GetString());
        Assert.Equal("POST", entry.GetProperty("HttpMethod").GetString());
        Assert.Equal("42", entry.GetProperty("UserId").GetString());
        Assert.Equal("test-correlation", entry.GetProperty("TraceId").GetString());
        Assert.DoesNotContain("query-secret", entry.GetRawText());
    }

    [Fact]
    public void Configured_secrets_credentials_and_jwts_are_redacted_from_all_diagnostic_text()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Jwt:AccessSecret"] = "configured-signing-secret",
            ["AdminBootstrap:Password"] = "configured-admin-password",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Password=database-password;Database=fitness"
        };
        using var provider = CreateProvider(configuration: configuration);
        var exception = CreateException("configured-signing-secret configured-admin-password database-password " +
            "Password=unconfigured-password; refresh_token='unconfigured refresh token'; " +
            "https://name:uri-password@example.test/path eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.signature");

        provider.CreateLogger("test").LogError(exception, "Failure with token={Token}", "opaque-access-token");

        var entry = ReadEntries().Single().GetRawText();
        foreach (var secret in new[] { "configured-signing-secret", "configured-admin-password", "database-password",
                     "unconfigured-password", "unconfigured refresh token", "uri-password", "eyJhbGciOiJIUzI1NiJ9", "opaque-access-token" })
            Assert.DoesNotContain(secret, entry);
        Assert.Contains("[REDACTED]", entry);
    }

    [Fact]
    public void Current_cookie_and_authorization_values_are_redacted_even_without_labels()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "access_token=cookie-access-secret; refresh_token=cookie-refresh-secret";
        context.Request.Headers.Authorization = "Bearer header-access-secret";
        using var provider = CreateProvider(context);

        provider.CreateLogger("test").LogError(CreateException("cookie-access-secret cookie-refresh-secret header-access-secret"),
            "Request processing failed.");

        var entry = ReadEntries().Single().GetRawText();
        Assert.DoesNotContain("cookie-access-secret", entry);
        Assert.DoesNotContain("cookie-refresh-secret", entry);
        Assert.DoesNotContain("header-access-secret", entry);
    }

    [Fact]
    public void Console_diagnostics_are_redacted_including_routine_levels()
    {
        using var output = new StringWriter();
        using var provider = CreateProvider(diagnosticWriter: output);
        var logger = provider.CreateLogger("test");

        logger.LogInformation("Received password={Password}", "console-password");
        logger.LogError(CreateException("Authorization: Bearer console-token"), "Request failed.");

        Assert.DoesNotContain("console-password", output.ToString());
        Assert.DoesNotContain("console-token", output.ToString());
        Assert.Contains("[REDACTED]", output.ToString());
        Assert.Contains("Information", output.ToString());
        Assert.Single(ReadEntries());
    }

    [Fact]
    public void Framework_formatted_uris_do_not_expose_query_values_in_console_or_files()
    {
        using var output = new StringWriter();
        using var provider = CreateProvider(diagnosticWriter: output);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");

        logger.LogInformation("Request starting HTTP/1.1 GET https://localhost/checkins?search=private-query&other=another-query");
        logger.LogError(CreateException("Failed at /checkins?search=private-query&other=another-query"), "Request failed.");

        var persisted = ReadEntries().Single().GetRawText();
        Assert.DoesNotContain("private-query", output.ToString());
        Assert.DoesNotContain("another-query", output.ToString());
        Assert.DoesNotContain("private-query", persisted);
        Assert.DoesNotContain("another-query", persisted);
        Assert.Contains("checkins?[REDACTED]", output.ToString());
    }

    [Fact]
    public void Cookie_and_authorization_assignments_with_equals_are_redacted()
    {
        using var output = new StringWriter();
        using var provider = CreateProvider(diagnosticWriter: output);

        provider.CreateLogger("test").LogError(CreateException("Cookie=session=opaque-session-value; preference=private-cookie\n" +
            "Authorization=Basic opaque-authorization-value"), "{Headers}", "Set-Cookie=session=opaque-response-cookie; HttpOnly");

        var persisted = ReadEntries().Single().GetRawText();
        foreach (var secret in new[] { "opaque-session-value", "private-cookie", "opaque-authorization-value", "opaque-response-cookie" })
        {
            Assert.DoesNotContain(secret, output.ToString());
            Assert.DoesNotContain(secret, persisted);
        }
    }

    [Fact]
    public void Http_logging_raw_formatter_is_omitted_before_headers_or_bodies_can_be_read()
    {
        using var output = new StringWriter();
        using var provider = CreateProvider(diagnosticWriter: output);
        var logger = provider.CreateLogger("Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware");
        var formatterCalled = false;

        logger.Log(LogLevel.Error, new EventId(1), "body-private-value", null, (state, _) =>
        {
            formatterCalled = true;
            return state;
        });

        Assert.False(formatterCalled);
        Assert.DoesNotContain("body-private-value", output.ToString());
        Assert.Equal("HTTP diagnostic details omitted.", ReadEntries().Single().GetProperty("Message").GetString());
    }

    [Fact]
    public void Configured_uri_password_and_escaped_json_credentials_are_redacted()
    {
        var configuration = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "postgresql://account:uri%40password@localhost/fitness"
        };
        using var provider = CreateProvider(configuration: configuration);

        provider.CreateLogger("test").LogError(CreateException("uri@password uri%40password {\"password\":\"quote\\\"secret\"}"),
            "Request failed.");

        var entry = ReadEntries().Single().GetRawText();
        Assert.DoesNotContain("uri@password", entry);
        Assert.DoesNotContain("uri%40password", entry);
        Assert.DoesNotContain("quote", entry);
        Assert.DoesNotContain("secret", entry);
    }

    [Fact]
    public void Console_write_failure_does_not_prevent_persistent_error_logging()
    {
        using var output = new ThrowingWriter();
        using var provider = CreateProvider(diagnosticWriter: output);

        var error = Record.Exception(() => provider.CreateLogger("test").LogError(CreateException("original failure"), "Request failed."));

        Assert.Null(error);
        Assert.Equal("original failure", ReadEntries().Single().GetProperty("ExceptionMessage").GetString());
    }

    [Fact]
    public void Existing_warning_logs_with_exceptions_are_persisted_but_routine_messages_are_ignored()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("test");

        logger.LogInformation("Routine information.");
        logger.LogWarning("Routine warning without exception.");
        logger.LogWarning(CreateException("Database unavailable"), "Unable to load catalog.");

        var entry = ReadEntries().Single();
        Assert.Equal("Warning", entry.GetProperty("Level").GetString());
        Assert.Equal("Unable to load catalog.", entry.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("UserId").ValueKind);
    }

    [Fact]
    public void Unwritable_log_directory_does_not_throw_or_overwrite_existing_file()
    {
        Directory.CreateDirectory(root);
        var blockedPath = Path.Combine(root, "blocked");
        File.WriteAllText(blockedPath, "existing content");
        using var provider = CreateProvider(directoryPath: "blocked");

        var error = Record.Exception(() => provider.CreateLogger("test").LogError(CreateException("original failure"), "Request failed."));

        Assert.Null(error);
        Assert.Equal("existing content", File.ReadAllText(blockedPath));
    }

    [Fact]
    public void Throwing_formatter_does_not_escape_and_subsequent_error_can_be_written()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("test");

        var error = Record.Exception(() => logger.Log(LogLevel.Error, new EventId(1), "state", CreateException("original failure"),
            (_, _) => throw new InvalidOperationException("formatter failure")));
        logger.LogError(CreateException("next failure"), "Next request failed.");

        Assert.Null(error);
        Assert.Equal("next failure", ReadEntries().Single().GetProperty("ExceptionMessage").GetString());
    }

    [Theory]
    [InlineData("wwwroot")]
    [InlineData("wwwroot/private-logs")]
    public void Log_storage_under_web_root_is_rejected(string path)
    {
        Assert.Throws<InvalidOperationException>(() => CreateProvider(directoryPath: path));
    }

    [Fact]
    public async Task Concurrent_error_entries_are_complete_independent_json_records()
    {
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("test");

        await Task.WhenAll(Enumerable.Range(1, 20).Select(index => Task.Run(() => logger.LogError("Failure {Index}", index),
            TestContext.Current.CancellationToken)));

        var entries = ReadEntries();
        Assert.Equal(20, entries.Count);
        Assert.Equal(20, entries.Select(entry => entry.GetProperty("Message").GetString()).Distinct().Count());
    }

    private ErrorFileLoggerProvider CreateProvider(HttpContext? context = null,
        Dictionary<string, string?>? configuration = null, string directoryPath = "logs", TextWriter? diagnosticWriter = null)
    {
        Directory.CreateDirectory(root);
        return new ErrorFileLoggerProvider(Options.Create(new ErrorFileLoggerOptions { DirectoryPath = directoryPath }),
            new TestEnvironment(root), new HttpContextAccessor { HttpContext = context },
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build(), diagnosticWriter ?? TextWriter.Null);
    }

    private List<JsonElement> ReadEntries() => Directory.GetFiles(Path.Combine(root, "logs"), "errors-*.jsonl")
        .SelectMany(File.ReadAllLines).Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

    private static Exception CreateException(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (InvalidOperationException exception) { return exception; }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class TestEnvironment(string rootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "FitnessApp.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = rootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(rootPath, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("The console is unavailable.");
    }
}
