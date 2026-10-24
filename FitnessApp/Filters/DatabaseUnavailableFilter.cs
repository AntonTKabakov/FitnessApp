using System.Data.Common;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Filters;

// Applied only to the new MVC screens; existing API error behavior is unchanged.
public class DatabaseUnavailableFilter(ILogger<DatabaseUnavailableFilter> logger, IModelMetadataProvider metadata) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not (DbException or DbUpdateException or TimeoutException)) return;
        logger.LogError(context.Exception, "Database operation failed for MVC route {Path}", context.HttpContext.Request.Path);
        var request = context.HttpContext.Request;
        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/Unavailable.cshtml",
            StatusCode = StatusCodes.Status503ServiceUnavailable,
            ViewData = new ViewDataDictionary<UnavailableViewModel>(metadata, context.ModelState)
            {
                Model = new UnavailableViewModel { ReturnUrl = HttpMethods.IsGet(request.Method) ? request.PathBase + request.Path + request.QueryString : "/" }
            }
        };
        context.ExceptionHandled = true;
    }
}
