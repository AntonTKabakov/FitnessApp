using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FitnessApp.Logging;

// Adds persistence to the application's existing ILogger calls, including handled database errors.
[ProviderAlias("ErrorFile")]
public sealed class ErrorFileLoggerProvider : ILoggerProvider
{
    private readonly IHttpContextAccessor accessor;
    private readonly ErrorLogRedactor redactor;
    private readonly string directory;
    private readonly TextWriter diagnosticWriter;
    private readonly object writeLock = new();
    private bool failureReported;

    public ErrorFileLoggerProvider(
        IOptions<ErrorFileLoggerOptions> options,
        IWebHostEnvironment environment,
        IHttpContextAccessor accessor,
        IConfiguration configuration,
        TextWriter? diagnosticWriter = null)
    {
        this.accessor = accessor;
        this.diagnosticWriter = diagnosticWriter ?? Console.Out;
        redactor = new ErrorLogRedactor(configuration);
        if (string.IsNullOrWhiteSpace(options.Value.DirectoryPath))
            throw new InvalidOperationException("ErrorLogging:DirectoryPath must be configured.");
        directory = Path.GetFullPath(options.Value.DirectoryPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (directory.Equals(webRoot, comparison) || directory.StartsWith(
                webRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("ErrorLogging:DirectoryPath must be outside the web root.");
    }

    public ILogger CreateLogger(string categoryName) => new ErrorFileLogger(this, categoryName);

    public void Dispose() { }

    private void Write<TState>(string category, LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (level == LogLevel.None) return;
        // Nothing in a logging provider, including formatting and redaction, may hide the original failure.
        lock (writeLock)
        {
            try
            {
                var context = accessor.HttpContext;
                var timestamp = DateTimeOffset.UtcNow;
                // Opting into HTTP logging later must not send request bodies or headers to this provider.
                var omitHttpDetails = category.StartsWith("Microsoft.AspNetCore.HttpLogging", StringComparison.Ordinal);
                var entry = new
                {
                    Timestamp = timestamp,
                    Level = level.ToString(),
                    Category = category,
                    EventId = eventId.Id,
                    Message = omitHttpDetails ? "HTTP diagnostic details omitted." : redactor.Redact(formatter(state, exception), context),
                    ExceptionType = exception?.GetType().FullName,
                    ExceptionMessage = redactor.Redact(exception?.Message, context),
                    StackTrace = redactor.Redact(exception?.StackTrace, context),
                    Exception = redactor.Redact(exception?.ToString(), context),
                    RequestPath = redactor.Redact(context == null ? null : context.Request.PathBase.Add(context.Request.Path).Value, context),
                    HttpMethod = context?.Request.Method,
                    UserId = context?.User.Identity?.IsAuthenticated == true
                        ? redactor.Redact(context.User.FindFirstValue(ClaimTypes.NameIdentifier), context) : null,
                    TraceId = context?.TraceIdentifier ?? Activity.Current?.TraceId.ToString()
                };
                var json = JsonSerializer.Serialize(entry);
                // Replaces the default console/debug providers so exception text is redacted in every sink.
                try { diagnosticWriter.WriteLine(json); }
                catch (Exception) { /* Console availability does not control durable logging. */ }
                if (level < LogLevel.Error && !(level == LogLevel.Warning && exception != null)) return;

                Directory.CreateDirectory(directory);
                EnsureNoLinks(directory);
                var path = Path.Combine(directory, $"errors-{timestamp:yyyy-MM-dd}.jsonl");
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Log files must not be symbolic links.");
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(json);
                failureReported = false;
            }
            catch (Exception)
            {
                if (failureReported) return;
                failureReported = true;
                try { Trace.TraceError("FitnessApp could not persist an error log entry. Check the private log directory and permissions."); }
                catch (Exception) { /* A failing diagnostic listener must not affect the request either. */ }
            }
        }
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The log directory must not contain symbolic links.");
    }

    private sealed class ErrorFileLogger(ErrorFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => provider.Write(category, level, eventId, state, exception, formatter);
    }
}
