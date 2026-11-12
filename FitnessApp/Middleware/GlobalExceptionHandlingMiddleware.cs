using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace FitnessApp.Middleware;

public class GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client has disconnected; there is no response left to write.
            context.Abort();
        }
        catch (Exception exception)
        {
            var statusCode = StatusCode(exception);
            Log(exception, statusCode);
            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            try
            {
                await ErrorResponse.WriteAsync(context, statusCode);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                context.Abort();
            }
            catch (Exception renderingException)
            {
                Log(renderingException, StatusCodes.Status500InternalServerError);
                if (context.Response.HasStarted) throw;
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.Headers.CacheControl = "no-store";
                await ErrorResponse.WriteJsonAsync(context, statusCode);
            }
        }
    }

    private void Log(Exception exception, int statusCode)
    {
        try
        {
            if (statusCode >= 500) logger.LogError(exception, "Request failed with status {StatusCode}.", statusCode);
            else logger.LogWarning(exception, "Request failed with status {StatusCode}.", statusCode);
        }
        catch
        {
            // A broken logging provider must never replace the original response.
        }
    }

    private static int StatusCode(Exception exception) => exception switch
    {
        BadHttpRequestException badRequest when badRequest.StatusCode is >= 400 and < 500 => badRequest.StatusCode,
        ValidationException or ArgumentException => StatusCodes.Status400BadRequest,
        AuthenticationException or SecurityTokenException => StatusCodes.Status401Unauthorized,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        KeyNotFoundException => StatusCodes.Status404NotFound,
        DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
        DbUpdateException { InnerException: PostgresException postgres } => DatabaseStatusCode(postgres),
        PostgresException postgres => DatabaseStatusCode(postgres),
        _ => StatusCodes.Status500InternalServerError
    };

    private static int DatabaseStatusCode(PostgresException exception) => exception.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation => StatusCodes.Status409Conflict,
        PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
}
