using FitnessApp.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using FitnessApp.Filters;
using FitnessApp.Service;

namespace FitnessApp.Controllers
{
    [TypeFilter(typeof(DatabaseUnavailableFilter))]
    public class HomeController(ReceptionQueryService queries) : Controller
    {
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            return View(await queries.Dashboard(cancellationToken));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
