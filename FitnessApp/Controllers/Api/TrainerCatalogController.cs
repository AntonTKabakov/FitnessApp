using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using FitnessApp.DTO.TrainerDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FitnessApp.Controllers.Api;

[ApiController, Route("api/trainers"), Authorize(Roles = UserRoles.Admin)]
[AutoValidateAntiforgeryToken]
public class TrainerCatalogController(AppDbContext db, ITrainerService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? search, int page = 1, CancellationToken ct = default)
    {
        var query = db.Trainers.AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Fullname.ToLower().Contains(search.ToLower()) || x.Specialty.ToLower().Contains(search.ToLower()) || x.Phone.Contains(search));
        var result = new CatalogListViewModel<TrainerFormViewModel> { Search = search, Total = await query.CountAsync(ct) };
        result.Page = Math.Clamp(page, 1, result.TotalPages);
        result.Items = await query.OrderBy(x => x.Fullname).ThenBy(x => x.Trainerid)
            .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize).Select(x => new TrainerFormViewModel { Id = x.Trainerid, FullName = x.Fullname, Specialty = x.Specialty, Phone = x.Phone, CheckinCount = x.Checkins.Count }).ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await db.Trainers.AsNoTracking().Where(x => x.Trainerid == id).Select(x => new TrainerFormViewModel { Id = x.Trainerid, FullName = x.Fullname, Specialty = x.Specialty, Phone = x.Phone, CheckinCount = x.Checkins.Count }).SingleOrDefaultAsync(ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public Task<IActionResult> Create(TrainerFormViewModel model, CancellationToken ct) { model.Id = null; return Save(model, ct); }

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, TrainerFormViewModel model, CancellationToken ct) { model.Id = id; return Save(model, ct); }

    private async Task<IActionResult> Save(TrainerFormViewModel model, CancellationToken ct)
    {
        model.FullName = model.FullName.Trim();
        if (model.FullName.Length == 0) ModelState.AddModelError(nameof(model.FullName), "This field is required.");
        model.Specialty = model.Specialty.Trim();
        if (model.Specialty.Length == 0) ModelState.AddModelError(nameof(model.Specialty), "This field is required.");
        model.Phone = model.Phone.Trim();
        if (model.Phone.Length == 0) ModelState.AddModelError(nameof(model.Phone), "This field is required.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var existing = model.Id.HasValue ? await db.Trainers.AsNoTracking().SingleOrDefaultAsync(x => x.Trainerid == model.Id, ct) : null;
        if (model.Id.HasValue && existing == null) return NotFound();
        if (await db.Trainers.AnyAsync(x => x.Phone == model.Phone && x.Trainerid != model.Id, ct))
            return Conflict(new { error = "An entry with this phone number already exists." });
        var dto = new TrainerEditDTO { Id = model.Id, Fullname = model.FullName, Specialty = model.Specialty, Phone = model.Phone };
        if (existing != null)
        {
            if (existing.Fullname == model.FullName && existing.Specialty == model.Specialty && existing.Phone == model.Phone) return NoContent();
            return service.Update(dto) ? NoContent() : Conflict(new { error = "The entry could not be updated." });
        }
        var created = service.Create(dto);
        return created.HasValue ? CreatedAtAction(nameof(Get), new { id = created.Value }, new { id = created.Value }) : Conflict(new { error = "The entry could not be created." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await db.Trainers.AnyAsync(x => x.Trainerid == id, ct)) return NotFound();
        if (await db.Trainers.AnyAsync(x => x.Trainerid == id && x.Checkins.Any(), ct))
            return Conflict(new { error = "This entry is in use. Its history must be preserved." });
        return service.Delete(id) ? NoContent() : Conflict(new { error = "The entry could not be deleted." });
    }
}

