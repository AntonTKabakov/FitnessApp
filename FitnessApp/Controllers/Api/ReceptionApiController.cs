using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using FitnessApp.DTO.CheckinDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers.Api;

[ApiController, Route("api"), Authorize, AutoValidateAntiforgeryToken]
public class ReceptionApiController(ReceptionQueryService queries, CurrentUser current,
    AppDbContext db, ICustomerPassService passes, ICheckinService checkins, AccountRoleService roles) : ControllerBase
{
    private int? Owner => current.IsAdmin ? null : current.Id;

    [HttpGet("dashboard"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await queries.Dashboard(ct));

    [HttpGet("members/me")]
    public async Task<IActionResult> Member(CancellationToken ct)
    {
        var model = await queries.Customer(current.Id, ct);
        return model == null ? NotFound() : Ok(model);
    }

    [HttpGet("customers"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Customers(string? query, int page = 1, CancellationToken ct = default) =>
        Ok(await queries.Customers(query, page, ct));

    [HttpGet("customers/{id:int}"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Customer(int id, CancellationToken ct)
    {
        var model = await queries.Customer(id, ct);
        return model == null ? NotFound() : Ok(model);
    }

    [HttpGet("customers/{id:int}/pass-options"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> PurchaseOptions(int id, CancellationToken ct)
    {
        var model = await queries.Purchase(id, ct);
        return model == null ? NotFound() : Ok(model);
    }

    [HttpPost("customers/{id:int}/passes"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Purchase(int id, PurchasePassViewModel model, CancellationToken ct)
    {
        if (!await db.Customers.AnyAsync(x => x.Id == id, ct)) return NotFound();
        var created = await passes.PurchaseAsync(id, model.PassTypeId!.Value, ct);
        return created.HasValue ? Created($"/api/customers/{id}", new { id = created.Value })
            : Conflict(new { error = "This pass type is no longer available or has invalid limits." });
    }

    [HttpGet("visits")]
    public async Task<IActionResult> Visits(string? query, DateOnly? date, int page = 1, CancellationToken ct = default) =>
        Ok(await queries.Visits(query, date, page, ct, Owner));

    [HttpGet("visits/options")]
    public async Task<IActionResult> Options(string? query, int? customerId, int? passId, CancellationToken ct)
    {
        if (!current.IsAdmin && passId.HasValue && !await db.Customerpasses.AnyAsync(x => x.Customerpassid == passId && x.Customerid == current.Id, ct)) return NotFound();
        var model = new CheckinFormViewModel { Query = query, CustomerPassId = passId };
        await queries.PopulateCheckin(model, customerId, ct, Owner);
        return Ok(model);
    }

    [HttpGet("visits/{id:int}")]
    public async Task<IActionResult> Visit(int id, CancellationToken ct)
    {
        var model = await queries.Visit(id, ct, Owner);
        return model == null ? NotFound() : Ok(model);
    }

    [HttpGet("visits/trainers")]
    public async Task<IActionResult> Trainers(CancellationToken ct) => Ok(await queries.Trainers(ct));

    [HttpPost("visits")]
    public async Task<IActionResult> CreateVisit(CheckinFormViewModel model, CancellationToken ct)
    {
        if (!current.IsAdmin && !await db.Customerpasses.AnyAsync(x => x.Customerpassid == model.CustomerPassId && x.Customerid == current.Id, ct)) return NotFound();
        if (model.TrainerId.HasValue && !await db.Trainers.AnyAsync(x => x.Trainerid == model.TrainerId, ct))
            return BadRequest(new { error = "Choose an available trainer." });
        var id = await checkins.CreateAsync(new CheckinEditDTO { CustomerPassId = model.CustomerPassId!.Value, TrainerId = model.TrainerId }, ct);
        return id.HasValue ? CreatedAtAction(nameof(Visit), new { id = id.Value }, new { id = id.Value })
            : Conflict(new { error = "Choose a valid pass with visits remaining that has not been checked in today." });
    }

    [HttpPut("visits/{id:int}"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> UpdateVisit(int id, VisitEditViewModel model, CancellationToken ct)
    {
        var visit = await queries.Visit(id, ct);
        if (visit == null) return NotFound();
        if (model.TrainerId.HasValue && !await db.Trainers.AnyAsync(x => x.Trainerid == model.TrainerId, ct)) return BadRequest(new { error = "Choose an available trainer." });
        return visit.TrainerId == model.TrainerId || checkins.Update(new CheckinEditDTO {
            CheckinId = id, CustomerPassId = visit.CustomerPassId, CheckinDateTime = visit.CheckedInAt, TrainerId = model.TrainerId })
            ? NoContent() : Conflict(new { error = "The check-in could not be updated." });
    }

    [HttpDelete("visits/{id:int}"), Authorize(Roles = UserRoles.Admin)]
    public IActionResult DeleteVisit(int id) => checkins.Delete(id) ? NoContent() : NotFound();

    [HttpGet("accounts"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Accounts(string? search, int page = 1, CancellationToken ct = default)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Email.ToLower().Contains(search.ToLower()) || x.Fullname.ToLower().Contains(search.ToLower()));
        var pagination = new PaginationViewModel { TotalCount = await query.CountAsync(ct) };
        pagination.Page = Math.Clamp(page, 1, pagination.TotalPages);
        return Ok(new AccountRolesViewModel { CurrentUserId = current.Id, Search = search, Pagination = pagination,
            Accounts = await query.OrderBy(x => x.Email).ThenBy(x => x.Id).Skip((pagination.Page - 1) * pagination.PageSize).Take(pagination.PageSize)
                .Select(x => new AccountRoleRowViewModel { Id = x.Id, Name = x.Fullname, Email = x.Email, Role = x.Role }).ToListAsync(ct) });
    }

    [HttpPut("accounts/{id:int}/role"), Authorize(Roles = UserRoles.Admin)]
    public async Task<IActionResult> Role(int id, ChangeRoleViewModel model, CancellationToken ct)
    {
        var error = await roles.ChangeAsync(current.Id, id, model.Role, ct);
        return error == null ? NoContent() : Conflict(new { error });
    }
}
