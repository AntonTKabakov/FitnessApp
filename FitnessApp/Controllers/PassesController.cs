using Microsoft.AspNetCore.Authorization;
using System.Data.Common;
using FitnessApp.Data;
using FitnessApp.DTO.GympassDTOs;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[Authorize(Roles = FitnessApp.Models.UserRoles.Admin)]
public sealed class PassesController(
    AppDbContext db, IGympassService passes, ILogger<PassesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = new CatalogListViewModel<PassFormViewModel> { Search = search?.Trim(), Page = Math.Clamp(page, 1, 100000) };
        try
        {
            var query = db.Gympasses.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(model.Search))
                query = query.Where(x => x.Passname.ToLower().Contains(model.Search.ToLower()));
            model.Total = await query.CountAsync(cancellationToken);
            model.Page = Math.Min(model.Page, model.TotalPages);
            model.Items = await query.OrderBy(x => x.Passname).ThenBy(x => x.Passtypeid)
                .Skip((model.Page - 1) * model.PageSize).Take(model.PageSize)
                .Select(x => new PassFormViewModel { Id = x.Passtypeid, Name = x.Passname, Price = x.Price,
                    MaxVisits = x.Maxvisits, ValidityDays = x.Validitydays, PurchasedCount = x.Customerpasses.Count })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to load the gym pass catalog.");
            model.Error = "The pass catalog could not be loaded. Check the database connection and try again.";
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new PassFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PassFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = null;
        return await Save(model, false, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> Edit(int id, CancellationToken cancellationToken) => Load(id, "Edit", cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PassFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        return await Save(model, true, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> Details(int id, CancellationToken cancellationToken) => Load(id, "Details", cancellationToken);

    [HttpGet]
    public Task<IActionResult> Delete(int id, CancellationToken cancellationToken) => Load(id, "Delete", cancellationToken);

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id, CancellationToken cancellationToken)
    {
        try
        {
            if (!await db.Gympasses.AnyAsync(x => x.Passtypeid == id, cancellationToken)) return NotFound();
            if (await db.Customerpasses.AnyAsync(x => x.Passtypeid == id, cancellationToken))
            {
                TempData["Error"] = "This pass type is used by customer passes and cannot be deleted.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (!passes.Delete(id))
                TempData["Error"] = "The pass could not be deleted. Please try again.";
            else
                TempData["Success"] = "Pass type deleted.";
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to delete gym pass {PassId}.", id);
            TempData["Error"] = "The pass could not be deleted. It may now be in use, or the database is unavailable.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Load(int id, string viewName, CancellationToken cancellationToken)
    {
        try
        {
            var model = await db.Gympasses.AsNoTracking().Where(x => x.Passtypeid == id)
                .Select(x => new PassFormViewModel { Id = x.Passtypeid, Name = x.Passname, Price = x.Price,
                    MaxVisits = x.Maxvisits, ValidityDays = x.Validitydays, PurchasedCount = x.Customerpasses.Count })
                .SingleOrDefaultAsync(cancellationToken);
            return model is null ? NotFound() : View(viewName, model);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to load gym pass {PassId}.", id);
            TempData["Error"] = "The pass could not be loaded. Check the database connection and try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> Save(PassFormViewModel model, bool editing, CancellationToken cancellationToken)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "A pass name is required.");
        if (!ModelState.IsValid) return View(editing ? "Edit" : "Create", model);
        try
        {
            if (editing && !await db.Gympasses.AnyAsync(x => x.Passtypeid == model.Id, cancellationToken)) return NotFound();
            if (await db.Gympasses.AnyAsync(x => x.Passname == model.Name && x.Passtypeid != model.Id, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.Name), "A pass with this name already exists.");
                return View(editing ? "Edit" : "Create", model);
            }
            var dto = new GympassEditDTO { PassTypeId = model.Id, PassName = model.Name, Price = model.Price,
                MaxVisits = model.MaxVisits, ValidityDays = model.ValidityDays };
            int? id = model.Id;
            if (editing)
            {
                // The existing service returns false for unchanged values, so recognize an unchanged form first.
                var current = await db.Gympasses.AsNoTracking().SingleAsync(x => x.Passtypeid == model.Id, cancellationToken);
                var unchanged = current.Passname == model.Name && current.Price == model.Price &&
                    current.Maxvisits == model.MaxVisits && current.Validitydays == model.ValidityDays;
                if (!unchanged && !passes.Update(dto)) id = null;
            }
            else id = passes.Create(dto);
            if (id.HasValue)
            {
                TempData["Success"] = editing ? "Pass type updated." : "Pass type created.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(string.Empty, "The pass could not be saved. Its name may already be in use; please try again.");
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to save gym pass {PassId}.", model.Id);
            ModelState.AddModelError(string.Empty, "The pass could not be saved. Check the database connection and try again.");
        }
        return View(editing ? "Edit" : "Create", model);
    }

    private static bool IsDatabaseError(Exception ex) => ex is DbException or DbUpdateException or TimeoutException;
}
