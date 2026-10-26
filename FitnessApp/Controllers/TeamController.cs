using Microsoft.AspNetCore.Authorization;
using System.Data.Common;
using FitnessApp.Data;
using FitnessApp.DTO.TrainerDTOs;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[Authorize(Roles = FitnessApp.Models.UserRoles.Admin)]
public sealed class TeamController(
    AppDbContext db, ITrainerService trainers, ILogger<TeamController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var model = new CatalogListViewModel<TrainerFormViewModel> { Search = search?.Trim(), Page = Math.Clamp(page, 1, 100000) };
        try
        {
            var query = db.Trainers.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(model.Search))
                query = query.Where(x => x.Fullname.ToLower().Contains(model.Search.ToLower()) ||
                    x.Specialty.ToLower().Contains(model.Search.ToLower()) || x.Phone.Contains(model.Search));
            model.Total = await query.CountAsync(cancellationToken);
            model.Page = Math.Min(model.Page, model.TotalPages);
            model.Items = await query.OrderBy(x => x.Fullname).ThenBy(x => x.Trainerid)
                .Skip((model.Page - 1) * model.PageSize).Take(model.PageSize)
                .Select(x => new TrainerFormViewModel { Id = x.Trainerid, FullName = x.Fullname,
                    Specialty = x.Specialty, Phone = x.Phone, CheckinCount = x.Checkins.Count })
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to load trainers.");
            model.Error = "The trainer list could not be loaded. Check the database connection and try again.";
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult Create() => View(new TrainerFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TrainerFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = null;
        return await Save(model, false, cancellationToken);
    }

    [HttpGet]
    public Task<IActionResult> Edit(int id, CancellationToken cancellationToken) => Load(id, "Edit", cancellationToken);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TrainerFormViewModel model, CancellationToken cancellationToken)
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
            if (!await db.Trainers.AnyAsync(x => x.Trainerid == id, cancellationToken)) return NotFound();
            if (await db.Checkins.AnyAsync(x => x.Trainerid == id, cancellationToken))
            {
                TempData["Error"] = "This trainer has recorded visits and cannot be deleted. Their history has been preserved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            if (!trainers.Delete(id))
                TempData["Error"] = "The trainer could not be deleted. Please try again.";
            else
                TempData["Success"] = "Trainer deleted.";
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to delete trainer {TrainerId}.", id);
            TempData["Error"] = "The trainer could not be deleted. Their records may now be in use, or the database is unavailable.";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> Load(int id, string viewName, CancellationToken cancellationToken)
    {
        try
        {
            var model = await db.Trainers.AsNoTracking().Where(x => x.Trainerid == id)
                .Select(x => new TrainerFormViewModel { Id = x.Trainerid, FullName = x.Fullname,
                    Specialty = x.Specialty, Phone = x.Phone, CheckinCount = x.Checkins.Count })
                .SingleOrDefaultAsync(cancellationToken);
            return model is null ? NotFound() : View(viewName, model);
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to load trainer {TrainerId}.", id);
            TempData["Error"] = "The trainer could not be loaded. Check the database connection and try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    private async Task<IActionResult> Save(TrainerFormViewModel model, bool editing, CancellationToken cancellationToken)
    {
        model.FullName = model.FullName?.Trim() ?? string.Empty;
        model.Specialty = model.Specialty?.Trim() ?? string.Empty;
        model.Phone = model.Phone?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(model.FullName)) ModelState.AddModelError(nameof(model.FullName), "A full name is required.");
        if (string.IsNullOrWhiteSpace(model.Specialty)) ModelState.AddModelError(nameof(model.Specialty), "A specialty is required.");
        if (string.IsNullOrWhiteSpace(model.Phone)) ModelState.AddModelError(nameof(model.Phone), "A phone number is required.");
        if (!ModelState.IsValid) return View(editing ? "Edit" : "Create", model);
        try
        {
            if (editing && !await db.Trainers.AnyAsync(x => x.Trainerid == model.Id, cancellationToken)) return NotFound();
            if (await db.Trainers.AnyAsync(x => x.Phone == model.Phone && x.Trainerid != model.Id, cancellationToken))
            {
                ModelState.AddModelError(nameof(model.Phone), "A trainer with this phone number already exists.");
                return View(editing ? "Edit" : "Create", model);
            }
            var dto = new TrainerEditDTO { Id = model.Id, Fullname = model.FullName, Specialty = model.Specialty, Phone = model.Phone };
            int? id = model.Id;
            if (editing)
            {
                var current = await db.Trainers.AsNoTracking().SingleAsync(x => x.Trainerid == model.Id, cancellationToken);
                var unchanged = current.Fullname == model.FullName && current.Specialty == model.Specialty && current.Phone == model.Phone;
                if (!unchanged && !trainers.Update(dto)) id = null;
            }
            else id = trainers.Create(dto);
            if (id.HasValue)
            {
                TempData["Success"] = editing ? "Trainer updated." : "Trainer added.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError(string.Empty, "The trainer could not be saved. Their phone number may already be in use; please try again.");
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogWarning(ex, "Unable to save trainer {TrainerId}.", model.Id);
            ModelState.AddModelError(string.Empty, "The trainer could not be saved. Check the database connection and try again.");
        }
        return View(editing ? "Edit" : "Create", model);
    }

    private static bool IsDatabaseError(Exception ex) => ex is DbException or DbUpdateException or TimeoutException;
}
