using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FitnessApp.Filters;

// The existing JWT handler prefers access_token cookies over Authorization headers.
// Cookie requests always require CSRF validation, even if a bearer header is also supplied.
public class ProfilePictureAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Cookies.ContainsKey("access_token")) return;
        if (string.IsNullOrWhiteSpace(context.HttpContext.Request.Headers["RequestVerificationToken"]))
        {
            context.Result = new BadRequestObjectResult(new { error = "A security token is required. Reload the page and try again." });
            return;
        }
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new { error = "Your security token has expired. Reload the page and try again." });
        }
    }
}
