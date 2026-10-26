using Microsoft.AspNetCore.Authorization;
using FitnessApp.Filters;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[TypeFilter(typeof(DatabaseUnavailableFilter))]
[Authorize(Roles = FitnessApp.Models.UserRoles.Admin)]
public class CustomersController(ReceptionQueryService queries, ICustomerPassService customerPasses) : Controller
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
        var model = await queries.Purchase(id, cancellationToken);
        return model == null ? NotFound() : View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Purchase(int id, PurchasePassViewModel model, CancellationToken cancellationToken)
    {
        var form = await queries.Purchase(id, cancellationToken);
        if (form == null) return NotFound();
        if (ModelState.IsValid)
        {
            var passId = await customerPasses.PurchaseAsync(id, model.PassTypeId!.Value, cancellationToken);
            if (passId.HasValue)
            {
                TempData["Success"] = "Pass assigned. It is ready to use for check-in.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(nameof(model.PassTypeId), "This pass type is no longer available or has invalid limits. Choose another pass type.");
        }
        form.PassTypeId = model.PassTypeId;
        return View(form);
    }
}
