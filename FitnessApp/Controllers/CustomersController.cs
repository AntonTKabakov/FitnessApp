using FitnessApp.Filters;
using FitnessApp.Service;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[TypeFilter(typeof(DatabaseUnavailableFilter))]
public class CustomersController(ReceptionQueryService queries) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? query, int page = 1, CancellationToken cancellationToken = default) =>
        View(await queries.Customers(query, page, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var model = await queries.Customer(id, cancellationToken);
        return model == null ? NotFound() : View(model);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpGet]
    public async Task<IActionResult> Purchase(int id, CancellationToken cancellationToken)
    {
        var model = await queries.Customer(id, cancellationToken);
        return model == null ? NotFound() : View(model);
    }
}
