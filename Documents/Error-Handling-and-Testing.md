# Error handling and automated tests

## Request errors

`GlobalExceptionHandlingMiddleware` runs before status-code pages, HTTPS handling,
routing, authentication, authorization, and controllers. It uses safe, fixed
messages in every environment, including Development. It never copies exception
messages or stack traces into HTTP responses. JWT challenges also omit token
validation exception details from their `WWW-Authenticate` headers.

| Failure | Status |
| --- | --- |
| Argument, validation, invalid HTTP request | 400 (or the HTTP request's specific 4xx status) |
| Authentication or token validation | 401 |
| Unauthorized operation | 403 |
| Missing entity | 404 |
| EF concurrency conflict, PostgreSQL unique or foreign-key constraint | 409 |
| PostgreSQL check or not-null constraint | 400 |
| Unexpected exception | 500 |

The middleware logs exceptions with `ILogger`. Expected 4xx exceptions are
warnings; unexpected failures are errors. A broken logger cannot replace the
original error response. A disconnected request is aborted without attempting
to send an error body. If a response has already started, its exception is
logged and rethrown because its headers and body cannot safely be replaced.

API errors use `application/problem+json` with a common shape:

```json
{
  "title": "Internal Server Error",
  "status": 500,
  "error": "We couldn't complete this request. Please try again or contact your administrator.",
  "traceId": "request-reference"
}
```

`ApiErrorResponseFilter` normalizes existing controller errors while preserving
their user-facing `error` messages and validation `errors`. Bodyless errors,
including authentication failures and missing routes, receive the same fields
through status-code pages. Existing successful responses and MVC form validation
are unchanged. `ApiValidationFilter` also handles model-binding failures on the
legacy `ControllerBase` APIs that do not have `[ApiController]`.

Browser requests accepting HTML receive the existing styled Razor 500 view or
the new 404 view, with the original error status, a request reference, and a
**Back to dashboard** link. API controller endpoints and `/api` or `/auth` URLs
always receive JSON. Unmatched routes return anonymous 404 responses; existing
application endpoints retain their authentication and role policies. Existing
MVC database-unavailable views and profile-picture error messages remain in use.
No separate frontend or additional MVC infrastructure was introduced.

The pass and trainer creation services no longer swallow database exceptions;
those failures now reach the global handler and persistent logger. The MVC
check-in screen was also updated to call the existing `CreateAsync` method,
resolving a compilation error. Both check-in entry points forward request
cancellation to that service.

## Persistent diagnostics

The existing `ILogger` abstraction is retained. `ErrorFileLoggerProvider`
replaces the default output providers so console and file output both pass
through the same redaction. Normal enabled diagnostics go to the console;
Error/Critical entries and Warning entries containing exceptions are also
appended as JSON lines to daily UTC files:

```text
FitnessApp/logs/errors-yyyy-MM-dd.jsonl
```

The default directory is relative to the application's content root. Configure
`ErrorLogging:DirectoryPath` (environment variable
`ErrorLogging__DirectoryPath`) for a private persistent production volume.
Directories under `wwwroot` are rejected. The default `logs` directory is
excluded from publish output and already covered by the repository's log ignore
rules. Log directories and files must not be symbolic links.

Each record includes UTC timestamp, level, logger category, event ID, diagnostic
message, exception type/message/stack/inner chain, request path without query,
HTTP method, authenticated user ID when available, and trace ID. The trace ID
matches the response reference. No headers, cookies, request bodies, or query
strings are recorded as request fields. Configured credentials, connection-string
passwords, current authentication values, JWTs, credential assignments, and URI
userinfo are redacted from diagnostic text. Query values are also scrubbed from
formatted framework messages, and raw HTTP logging payloads are suppressed.
Console and file write failures are
contained; a generic diagnostic reports a persistent write failure without
printing the original exception or credentials.

Give the application write permission to the private log directory. Back it up
and configure retention externally: the provider creates daily files but does
not delete old files. A full or unwritable disk cannot provide durable logging;
it will not prevent the original HTTP error response from being returned.

## Tests and execution

The existing `FitnessApp.Tests` project still targets .NET 10 and uses xUnit v3
and standard assertions. The only added NuGet package is **Moq 4.20.72**, used
for password hashing, HTTP context access, and logger failure isolation.

Business-service tests exercise the actual services. Because the services
directly depend on `AppDbContext`, each service test owns a SQLite in-memory
database rather than mocked EF query providers or a new repository layer.
No unit test connects to PostgreSQL. Existing integration tests use an isolated
SQLite database and now use ephemeral antiforgery keys and private test logs,
independent of the host user's Windows key ring.

| New test class | Coverage |
| --- | --- |
| `CheckinServiceTests` | Successful creation with trainer, missing/expired/empty pass, duplicate day, previous-day eligibility, refund exactly once, missing update |
| `CustomerPassServiceTests` | Purchase allowance and dates, missing customer/pass, invalid visits/price/duration and date overflow |
| `AuthServiceTests` | Successful/failed login, normalized registration and duplicate email, password rehash, refresh-token hashing/rotation/replay/expiry, logout isolation |
| `TokenServiceTests` | JWT signature, user/role claims, lifetime and rejection with another signing key |
| `GlobalErrorHandlingTests` | Exception mapping, safe production JSON/headers, validation, browser 404/500, unchanged access rules, trace persistence, logger failure and canceled/started responses |
| `PersistentErrorLoggingTests` | Metadata, credential redaction, console output, failing storage/formatter/console, private paths and concurrent records |

Run from the repository root:

```powershell
dotnet restore FitnessApp.slnx
dotnet build FitnessApp.slnx --no-restore -p:TailwindPowerShell=pwsh
dotnet test FitnessApp.slnx --no-build --no-restore --logger "trx;LogFileName=fitness-tests.trx" --results-directory artifacts/test-results
```

The PowerShell override uses the installed PowerShell 7 for the existing CSS
build script; omit it when the default Windows PowerShell installation works.
The checked-in Tailwind CSS is rebuilt through the existing pipeline.

Final verification: the complete solution built successfully. `dotnet test`
completed with **155 total cases: 151 passed, 0 failed, 4 skipped**. This includes
**75 added cases**: 32 business-service cases, 28 global-error cases, and 15
persistent-logging cases. The four skips are the three existing Windows
symbolic-link exclusions and the opt-in PostgreSQL statistics test without its
connection setting. The machine-readable report is
`artifacts/test-results/fitness-tests.trx` (ignored build artifact).

SQLite tests do not prove PostgreSQL query translation, concurrent serializable
transactions, or migrations. The existing opt-in PostgreSQL statistics test
remains separate and is skipped when its test connection is not supplied.
Existing symbolic-link storage tests are skipped on Windows by their current
test attributes.

Request-handling verification covers exceptions during HTTP processing, not
startup configuration failures or background jobs. Existing nullable-property
warnings in gym-pass/trainer DTOs remain outside this change.

## Files

Created:

- `FitnessApp/Middleware/ErrorResponse.cs`
- `FitnessApp/Middleware/GlobalExceptionHandlingMiddleware.cs`
- `FitnessApp/Filters/ApiErrorResponseFilter.cs`
- `FitnessApp/Logging/ErrorFileLoggerOptions.cs`
- `FitnessApp/Logging/ErrorFileLoggerProvider.cs`
- `FitnessApp/Logging/ErrorLogRedactor.cs`
- `FitnessApp/Views/Shared/NotFound.cshtml`
- `FitnessApp.Tests/AuthServiceTests.cs`
- `FitnessApp.Tests/CheckinServiceTests.cs`
- `FitnessApp.Tests/CustomerPassServiceTests.cs`
- `FitnessApp.Tests/GlobalErrorHandlingTests.cs`
- `FitnessApp.Tests/PersistentErrorLoggingTests.cs`
- `FitnessApp.Tests/ServiceTestDatabase.cs`
- `FitnessApp.Tests/TokenServiceTests.cs`
- `Documents/Error-Handling-and-Testing.md`

Modified:

- `FitnessApp/Program.cs`
- `FitnessApp/appsettings.json`
- `FitnessApp/FitnessApp.csproj`
- `FitnessApp/Controllers/CheckinController.cs`
- `FitnessApp/Controllers/HomeController.cs`
- `FitnessApp/Controllers/VisitsController.cs`
- `FitnessApp/Service/GympassService.cs`
- `FitnessApp/Service/TrainerService.cs`
- `FitnessApp/Views/Shared/Error.cshtml`
- `FitnessApp/wwwroot/css/tailwind.css` was regenerated by the CSS build with no semantic content change.
- `FitnessApp.Tests/FitnessApp.Tests.csproj`
- `FitnessApp.Tests/FitnessAppFactory.cs`
- `README.md`

No database schema or migrations were changed. Preexisting staged changes to
`.gitignore` and `appsettings.Development.json` were preserved.

Design references: [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0)
and [EF Core testing without the production database](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database).
