using FitnessApp.Filters;
using FitnessApp.Service;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[TypeFilter(typeof(DatabaseUnavailableFilter))]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public class MemberController(ReceptionQueryService queries, CurrentUser currentUser) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await queries.Customer(currentUser.Id, cancellationToken);
        return model == null ? NotFound() : View(model);
    }
}
