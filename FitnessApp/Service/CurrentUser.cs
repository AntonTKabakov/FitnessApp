using System.Security.Claims;
using FitnessApp.Models;

namespace FitnessApp.Service;

public class CurrentUser(IHttpContextAccessor accessor)
{
    public int Id => int.Parse(accessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole(UserRoles.Admin) == true;
}
