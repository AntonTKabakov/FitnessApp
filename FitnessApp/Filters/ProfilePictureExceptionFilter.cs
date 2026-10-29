using System.Data.Common;
using FitnessApp.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Filters;

public class ProfilePictureExceptionFilter(ILogger<ProfilePictureExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is ProfilePictureException pictureError)
        {
            context.Result = new ObjectResult(new { error = pictureError.Message }) { StatusCode = pictureError.StatusCode };
        }
        else if (context.Exception is DbException or DbUpdateException or TimeoutException)
        {
            logger.LogError(context.Exception, "Profile picture database operation failed.");
            context.Result = new ObjectResult(new { error = "Your profile is temporarily unavailable. Please try again." })
                { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
        else return;
        context.ExceptionHandled = true;
    }
}
