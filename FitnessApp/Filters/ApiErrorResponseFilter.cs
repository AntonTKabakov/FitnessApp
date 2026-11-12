using FitnessApp.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FitnessApp.Filters;

// Preserve the existing `error` property and validation errors while adding standard problem fields.
public class ApiErrorResponseFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not ObjectResult { StatusCode: >= 400 } result) return;
        var statusCode = result.StatusCode.Value;
        var problem = result.Value as ProblemDetails;
        if (problem == null)
        {
            var message = result.Value?.GetType().GetProperty("error")?.GetValue(result.Value) as string;
            problem = ErrorResponse.Create(context.HttpContext, statusCode, message);
        }
        else
        {
            problem.Status = statusCode;
            problem.Extensions["error"] = problem.Detail ?? problem.Title ?? ErrorResponse.Message(statusCode);
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        }
        result.Value = problem;
        result.DeclaredType = problem.GetType();
        result.ContentTypes.Clear();
        result.ContentTypes.Add("application/problem+json");
        context.HttpContext.Response.Headers.CacheControl = "no-store";
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}

// The legacy ControllerBase APIs do not use [ApiController]. Validate binding failures there too.
public class ApiValidationFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action &&
            !typeof(Controller).IsAssignableFrom(action.ControllerTypeInfo) && !context.ModelState.IsValid)
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(context.ModelState));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
