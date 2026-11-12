using FitnessApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.WebUtilities;

namespace FitnessApp.Middleware;

public static class ErrorResponse
{
    public static string Message(int statusCode) => statusCode switch
    {
        400 => "The request is invalid. Check your input and try again.",
        401 => "Please sign in to continue.",
        403 => "You do not have permission to perform this action.",
        404 => "The requested resource could not be found.",
        409 => "This operation conflicts with existing data. Reload and try again.",
        500 => "We couldn't complete this request. Please try again or contact your administrator.",
        503 => "This service is temporarily unavailable. Please try again.",
        _ => "The request could not be completed. Please check your request and try again."
    };

    public static ProblemDetails Create(HttpContext context, int statusCode, string? message = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode)
        };
        problem.Extensions["error"] = message ?? Message(statusCode);
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }

    public static bool WantsHtml(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")) return false;
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        // API endpoints always receive JSON, including requests made in a browser.
        if (action != null && !typeof(Controller).IsAssignableFrom(action.ControllerTypeInfo)) return false;
        return context.Request.GetTypedHeaders().Accept?.Any(x =>
            x.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) && x.Quality.GetValueOrDefault(1) > 0) == true;
    }

    public static async Task WriteAsync(HttpContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Headers.CacheControl = "no-store";
        if (WantsHtml(context) && statusCode is 404 or 500)
        {
            var view = new ViewResult
            {
                ViewName = statusCode == 404 ? "~/Views/Shared/NotFound.cshtml" : "~/Views/Shared/Error.cshtml",
                StatusCode = statusCode,
                ViewData = new ViewDataDictionary<ErrorViewModel>(
                    context.RequestServices.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary())
                {
                    Model = new ErrorViewModel { RequestId = context.TraceIdentifier }
                }
            };
            await view.ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()));
            return;
        }

        await WriteJsonAsync(context, statusCode);
    }

    public static Task WriteJsonAsync(HttpContext context, int statusCode) =>
        context.Response.WriteAsJsonAsync(Create(context, statusCode), options: null,
            contentType: "application/problem+json", cancellationToken: context.RequestAborted);
}
