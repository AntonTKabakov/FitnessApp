using System.Data.Common;
using System.Text.RegularExpressions;

namespace FitnessApp.Logging;

// Only diagnostic text is accepted here. Request bodies, query strings and headers are never logged.
internal sealed class ErrorLogRedactor
{
    private const string Replacement = "[REDACTED]";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex AuthenticationHeader = new(
        @"(?im)([""']?\b(?:authorization|proxy-authorization|cookie|set-cookie)[""']?\s*[:=]\s*)[^\r\n]+",
        RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex QueryString = new(
        @"\?[^\s#""']+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CredentialAssignment = new(
        @"(?i)([""']?(?:password|pwd|passphrase|client[_-]?secret|access[_-]?secret|signing[_-]?key|api[_-]?key|access[_-]?token|refresh[_-]?token|token|secret)[""']?\s*[:=]\s*)(?:""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^;,&\r\n]+)",
        RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex UriCredentials = new(
        @"(?i)\b([a-z][a-z0-9+.-]*://)[^/\s@]+@", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex BearerToken = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Jwt = new(
        @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.CultureInvariant, RegexTimeout);
    private readonly string[] configuredSecrets;

    public ErrorLogRedactor(IConfiguration configuration)
    {
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in configuration.AsEnumerable())
        {
            if (string.IsNullOrEmpty(item.Value)) continue;
            if (item.Key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains("SigningKey", StringComparison.OrdinalIgnoreCase) ||
                item.Key.Contains("ApiKey", StringComparison.OrdinalIgnoreCase))
                secrets.Add(item.Value);

            if (!item.Key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase)) continue;
            secrets.Add(item.Value);
            if (Uri.TryCreate(item.Value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
            {
                secrets.Add(uri.UserInfo);
                var separator = uri.UserInfo.IndexOf(':');
                if (separator >= 0 && separator < uri.UserInfo.Length - 1)
                {
                    var password = uri.UserInfo[(separator + 1)..];
                    secrets.Add(password);
                    secrets.Add(Uri.UnescapeDataString(password));
                }
            }
            try
            {
                var connection = new DbConnectionStringBuilder { ConnectionString = item.Value };
                foreach (string key in connection.Keys)
                    if ((key.Equals("Password", StringComparison.OrdinalIgnoreCase) || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)) &&
                        connection[key] is string password && password.Length > 0)
                        secrets.Add(password);
            }
            catch (ArgumentException)
            {
                // URI-style connection strings are handled by the URI rule below.
            }
        }
        configuredSecrets = secrets.OrderByDescending(x => x.Length).ToArray();
    }

    public string? Redact(string? value, HttpContext? context)
    {
        if (string.IsNullOrEmpty(value)) return value;
        foreach (var secret in configuredSecrets) value = value.Replace(secret, Replacement, StringComparison.Ordinal);

        // Values are used only for redaction; no request authentication material is serialized.
        if (context != null)
        {
            foreach (var cookie in context.Request.Cookies)
                if (!string.IsNullOrEmpty(cookie.Value)) value = value.Replace(cookie.Value, Replacement, StringComparison.Ordinal);
            foreach (var header in context.Request.Headers.Authorization)
            {
                if (string.IsNullOrEmpty(header)) continue;
                value = value.Replace(header, Replacement, StringComparison.Ordinal);
                var separator = header.IndexOf(' ');
                if (separator >= 0 && separator < header.Length - 1)
                    value = value.Replace(header[(separator + 1)..], Replacement, StringComparison.Ordinal);
            }
        }

        value = AuthenticationHeader.Replace(value, "$1" + Replacement);
        // Framework diagnostics may put the full URI in their formatted message, independently of RequestPath.
        value = QueryString.Replace(value, "?" + Replacement);
        value = CredentialAssignment.Replace(value, "$1" + Replacement);
        value = UriCredentials.Replace(value, "$1" + Replacement + "@");
        value = BearerToken.Replace(value, "Bearer " + Replacement);
        return Jwt.Replace(value, Replacement);
    }
}
