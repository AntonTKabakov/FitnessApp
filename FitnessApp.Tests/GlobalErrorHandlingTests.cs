using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using FitnessApp.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Npgsql;

namespace FitnessApp.Tests;

public sealed class GlobalErrorHandlingTests
{
    private const string PrivateMessage = "Internal-failure-marker; Password=private-password; access_token=private-token; Server=private-database";

    [Theory]
    [InlineData("argument", 400)]
    [InlineData("validation", 400)]
    [InlineData("bad-request", 400)]
    [InlineData("authentication", 401)]
    [InlineData("token", 401)]
    [InlineData("forbidden", 403)]
    [InlineData("missing", 404)]
    [InlineData("concurrency", 409)]
    [InlineData("duplicate", 409)]
    [InlineData("foreign-key", 409)]
    [InlineData("unexpected", 500)]
    public async Task Exceptions_return_the_expected_status_and_safe_correlated_json(string scenario, int status)
    {
        // Arrange
        using var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        var context = NewContext(services);
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw Failure(scenario),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        var json = ReadBody(context);
        using var response = JsonDocument.Parse(json);
        Assert.Equal(status, response.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(context.TraceIdentifier, response.RootElement.GetProperty("traceId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(response.RootElement.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(response.RootElement.GetProperty("title").GetString()));
        Assert.DoesNotContain("Internal-failure-marker", json);
        Assert.DoesNotContain("private-password", json);
        Assert.DoesNotContain("private-token", json);
        Assert.DoesNotContain("private-database", json);
        Assert.DoesNotContain("stackTrace", json);
        Assert.DoesNotContain(Failure(scenario).GetType().FullName!, json);
    }

    [Fact]
    public async Task A_failing_logger_does_not_replace_the_original_error_response()
    {
        // Arrange
        using var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        var context = NewContext(services);
        var logger = new Mock<ILogger<GlobalExceptionHandlingMiddleware>>();
        logger.Setup(x => x.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws(new IOException("Logging storage is unavailable."));
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw Failure("unexpected"), logger.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("Logging storage", ReadBody(context));
        using var response = JsonDocument.Parse(ReadBody(context));
        Assert.Equal(context.TraceIdentifier, response.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task A_started_response_is_not_replaced_with_partial_error_json()
    {
        // Arrange
        using var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        var context = NewContext(services);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var original = Failure("unexpected");
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw original,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        // Act
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));

        // Assert
        Assert.Same(original, exception);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_disconnected_request_does_not_generate_a_server_error_body()
    {
        // Arrange
        using var services = new ServiceCollection().AddOptions().AddLogging().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var context = NewContext(services);
        context.RequestAborted = cancellation.Token;
        var middleware = new GlobalExceptionHandlingMiddleware(_ => throw new OperationCanceledException(cancellation.Token),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.NotEqual(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Empty(ReadBody(context));
    }

    [Theory]
    [InlineData("invalid", 400)]
    [InlineData("unauthenticated", 401)]
    [InlineData("denied", 403)]
    [InlineData("absent", 404)]
    [InlineData("conflict", 409)]
    [InlineData("failure", 500)]
    public async Task Production_pipeline_returns_consistent_api_errors_even_when_html_is_accepted(string scenario, int status)
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        // Act
        using var response = await client.GetAsync("/test-errors/api/" + scenario, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
        Assert.DoesNotContain("Internal-failure-marker", text);
        Assert.DoesNotContain("private-password", text);
        Assert.DoesNotContain("private-token", text);
        if (scenario == "conflict")
            Assert.Equal("Membership already exists.", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Api_validation_preserves_field_errors_and_adds_the_common_error_contract()
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);

        // Act
        using var response = await client.PostAsJsonAsync("/test-errors/api/validate", new { }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("/a-route-that-does-not-exist", 404, "Page not found")]
    [InlineData("/test-errors/browser/failure", 500, "Something went wrong")]
    public async Task Browser_errors_render_custom_pages_with_navigation_and_the_original_status(string route, int status, string heading)
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        // Act
        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(heading, html);
        Assert.Contains("Back to dashboard", html);
        Assert.Contains("/css/tailwind.css", html);
        Assert.DoesNotContain("Internal-failure-marker", html);
        Assert.DoesNotContain("private-password", html);
        Assert.DoesNotContain("private-token", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unknown_api_route_returns_json_404_without_an_authentication_redirect(bool acceptsHtml)
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);
        if (acceptsHtml) client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        // Act
        using var response = await client.GetAsync("/api/no-such-resource", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Authentication_and_role_failures_use_json_without_changing_access_rules()
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);

        // Act: an anonymous request cannot read a protected API.
        using var anonymous = await client.GetAsync("/Checkin/GetOne?id=1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var anonymousJson = JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(401, anonymousJson.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(anonymousJson.RootElement.GetProperty("error").GetString()));

        // Arrange: registration creates a regular member and issues the existing auth cookies.
        using var registration = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "error-tests@members.test", password = FitnessAppFactory.Password,
            username = "Error test member", dateofbirth = "1990-01-01"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);

        // Act: an authenticated member still cannot manage the gym-pass catalog.
        using var forbidden = await client.GetAsync("/Gympass/GetOne?id=1", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Null(forbidden.Headers.Location);
        using var forbiddenJson = JsonDocument.Parse(await forbidden.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(403, forbiddenJson.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(forbiddenJson.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Invalid_jwt_returns_safe_json_without_validation_details_in_challenge_headers()
    {
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "private-invalid-token");

        using var response = await client.GetAsync("/Checkin/GetOne?id=1", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Null(challenge.Parameter);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("private-invalid-token", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(401, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Production_failures_persist_the_response_reference_and_request_context_without_secrets()
    {
        // Arrange
        await using var fixture = new FitnessAppFactory();
        await using var application = ProductionApplication(fixture);
        using var client = NewClient(application);
        using var registration = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "logged-error@members.test", password = FitnessAppFactory.Password,
            username = "Logged error member", dateofbirth = "1990-01-01"
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        using var account = JsonDocument.Parse(await registration.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var userId = account.RootElement.GetProperty("id").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        client.DefaultRequestHeaders.Add("X-Test-Secret", "private-header-marker");

        // Act
        using var response = await client.GetAsync("/test-errors/api/failure?refresh_token=private-query-marker",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var traceId = problem.RootElement.GetProperty("traceId").GetString();
        var lines = Directory.GetFiles(Path.Combine(fixture.Root, "logs"), "errors-*.jsonl")
            .SelectMany(File.ReadAllLines).ToArray();
        var matching = new List<JsonElement>();
        foreach (var line in lines)
        {
            using var entry = JsonDocument.Parse(line);
            if (entry.RootElement.GetProperty("TraceId").GetString() == traceId &&
                entry.RootElement.GetProperty("ExceptionType").GetString() == typeof(InvalidOperationException).FullName)
                matching.Add(entry.RootElement.Clone());
        }
        var error = Assert.Single(matching);
        Assert.Equal("/test-errors/api/failure", error.GetProperty("RequestPath").GetString());
        Assert.Equal("GET", error.GetProperty("HttpMethod").GetString());
        Assert.Equal(userId, error.GetProperty("UserId").GetString());
        Assert.True(error.GetProperty("Timestamp").TryGetDateTimeOffset(out _));
        Assert.Contains("Internal-failure-marker", error.GetProperty("ExceptionMessage").GetString());
        Assert.Contains("[REDACTED]", error.GetProperty("ExceptionMessage").GetString());
        Assert.Contains(nameof(ErrorProbeApiController), error.GetProperty("StackTrace").GetString());
        var text = string.Join(Environment.NewLine, lines);
        Assert.DoesNotContain("private-password", text);
        Assert.DoesNotContain("private-token", text);
        Assert.DoesNotContain("private-query-marker", text);
        Assert.DoesNotContain("private-header-marker", text);
        Assert.DoesNotContain(FitnessAppFactory.Password, text);
    }

    private static DefaultHttpContext NewContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "test-error-correlation" };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/unit-test";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context) =>
        System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private static Exception Failure(string scenario) => scenario switch
    {
        "argument" => new ArgumentException(PrivateMessage),
        "validation" => new ValidationException(PrivateMessage),
        "bad-request" => new BadHttpRequestException(PrivateMessage),
        "authentication" => new AuthenticationException(PrivateMessage),
        "token" => new SecurityTokenValidationException(PrivateMessage),
        "forbidden" => new UnauthorizedAccessException(PrivateMessage),
        "missing" => new KeyNotFoundException(PrivateMessage),
        "concurrency" => new DbUpdateConcurrencyException(PrivateMessage),
        "duplicate" => new DbUpdateException(PrivateMessage, new PostgresException(PrivateMessage, "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)),
        "foreign-key" => new DbUpdateException(PrivateMessage, new PostgresException(PrivateMessage, "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation)),
        _ => new InvalidOperationException(PrivateMessage)
    };

    private static WebApplicationFactory<Program> ProductionApplication(FitnessAppFactory fixture) =>
        fixture.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
                services.AddControllersWithViews().AddApplicationPart(typeof(ErrorProbeApiController).Assembly));
        });

    private static HttpClient NewClient(WebApplicationFactory<Program> application) =>
        application.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}

// Loaded only by this test fixture; no probe endpoint is included in the application.
[ApiController]
[AllowAnonymous]
[Route("test-errors/api")]
public sealed class ErrorProbeApiController : ControllerBase
{
    [HttpGet("{scenario}")]
    public IActionResult Probe(string scenario) => scenario switch
    {
        "invalid" => BadRequest(new { error = "Choose a valid pass." }),
        "unauthenticated" => Unauthorized(),
        "denied" => StatusCode(StatusCodes.Status403Forbidden),
        "absent" => NotFound(),
        "conflict" => Conflict(new { error = "Membership already exists." }),
        _ => throw new InvalidOperationException("Internal-failure-marker; Password=private-password; access_token=private-token")
    };

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ErrorProbeRequest request) => Ok(request.Name);
}

public sealed class ErrorProbeRequest
{
    [Required]
    public string? Name { get; set; }
}

[AllowAnonymous]
[Route("test-errors/browser")]
public sealed class ErrorProbeBrowserController : Controller
{
    [HttpGet("failure")]
    public IActionResult Failure() =>
        throw new InvalidOperationException("Internal-failure-marker; Password=private-password; access_token=private-token");
}
