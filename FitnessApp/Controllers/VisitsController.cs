using FitnessApp.Data;
using FitnessApp.Models;
using Microsoft.AspNetCore.Authorization;
using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Filters;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[TypeFilter(typeof(DatabaseUnavailableFilter))]
public class VisitsController(ReceptionQueryService queries, ICheckinService checkins, AppDbContext db, CurrentUser currentUser) : Controller
{
    private int? OwnerId => currentUser.IsAdmin ? null : currentUser.Id;
    private IActionResult Screen(string name, object model) => View($"~/Views/Checkin/{name}.cshtml", model);

    [HttpGet]
    public async Task<IActionResult> Index(string? query, DateOnly? date, int page = 1, CancellationToken cancellationToken = default) =>
        Screen("Index", await queries.Visits(query, date, page, cancellationToken, OwnerId));

    [HttpGet]
    public async Task<IActionResult> Create(int? customerId, int? passId, string? query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAdmin && passId.HasValue &&
            !await db.Customerpasses.AnyAsync(x => x.Customerpassid == passId && x.Customerid == currentUser.Id, cancellationToken))
            return NotFound();
        var model = new CheckinFormViewModel { CustomerPassId = passId, Query = query };
        await queries.PopulateCheckin(model, customerId, cancellationToken, OwnerId);
        return Screen("Create", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CheckinFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var today = ReceptionTime.Today;
            var dayStart = today.ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var pass = await db.Customerpasses.AsNoTracking().SingleOrDefaultAsync(x => x.Customerpassid == model.CustomerPassId, cancellationToken);
            if (!currentUser.IsAdmin && (pass == null || pass.Customerid != currentUser.Id)) return NotFound();
            if (pass == null || pass.Expirydate < today || pass.Remainingvisits <= 0)
                ModelState.AddModelError(nameof(model.CustomerPassId), "Choose a valid pass with visits remaining.");
            else if (await db.Checkins.AnyAsync(x => x.Customerpassid == pass.Customerpassid && x.Checkindatetime >= dayStart && x.Checkindatetime < dayEnd, cancellationToken))
                ModelState.AddModelError(nameof(model.CustomerPassId), "This pass has already been checked in today.");
            if (model.TrainerId.HasValue && !await db.Trainers.AnyAsync(x => x.Trainerid == model.TrainerId, cancellationToken))
                ModelState.AddModelError(nameof(model.TrainerId), "Choose an available trainer.");
            if (ModelState.IsValid)
            {
                var id = checkins.Create(new CheckinEditDTO { CustomerPassId = model.CustomerPassId!.Value, TrainerId = model.TrainerId });
                if (id.HasValue)
                {
                    TempData["Success"] = "Entry confirmed. One visit has been deducted from the pass.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                ModelState.AddModelError("", "Entry could not be confirmed. The pass may have changed; refresh and try again.");
            }
        }
        await queries.PopulateCheckin(model, null, cancellationToken, OwnerId);
        return Screen("Create", model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var model = await queries.Visit(id, cancellationToken, OwnerId);
        return model == null ? NotFound() : Screen("Details", model);
    }

    [HttpGet, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var visit = await queries.Visit(id, cancellationToken);
        if (visit == null) return NotFound();
        return Screen("Edit", new VisitEditViewModel { Id = id, TrainerId = visit.TrainerId, Visit = visit, Trainers = await queries.Trainers(cancellationToken) });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Edit(int id, VisitEditViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        var visit = await queries.Visit(id, cancellationToken);
        if (visit == null) return NotFound();
        if (model.TrainerId.HasValue && !await db.Trainers.AnyAsync(x => x.Trainerid == model.TrainerId, cancellationToken))
            ModelState.AddModelError(nameof(model.TrainerId), "Choose an available trainer.");
        if (ModelState.IsValid)
        {
            // Customer pass and timestamp are deliberately sourced from the database.
            // The existing Update API allows reassignment but does not reconcile visit balances.
            if (visit.TrainerId == model.TrainerId || checkins.Update(new CheckinEditDTO
                { CheckinId = id, CustomerPassId = visit.CustomerPassId, CheckinDateTime = visit.CheckedInAt, TrainerId = model.TrainerId }))
            {
                TempData["Success"] = "Trainer updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            ModelState.AddModelError("", "The check-in could not be updated. Please try again.");
        }
        model.Visit = visit;
        model.Trainers = await queries.Trainers(cancellationToken);
        return Screen("Edit", model);
    }

    [HttpGet, Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var model = await queries.Visit(id, cancellationToken);
        return model == null ? NotFound() : Screen("Delete", model);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = UserRoles.Admin)]
    public IActionResult DeleteConfirmed(int id)
    {
        if (!checkins.Delete(id)) return NotFound();
        TempData["Success"] = "Check-in removed. One visit has been restored to the pass.";
        return RedirectToAction(nameof(Index));
    }
}
