using FitnessApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FitnessApp.Filters;
using FitnessApp.Service;

namespace FitnessApp.Controllers
{
    [TypeFilter(typeof(DatabaseUnavailableFilter))]
    public class HomeController(ReceptionQueryService queries) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            if (!User.IsInRole(UserRoles.Admin)) return RedirectToAction("Index", "Member");
            return View(await queries.Dashboard(cancellationToken));
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        [AllowAnonymous]
        public IActionResult Error()
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
        }
    }
}
