using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using FitnessApp.DTO.GympassDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace FitnessApp.Controllers.Api;

[ApiController, Route("api/passes"), Authorize(Roles = UserRoles.Admin)]
[AutoValidateAntiforgeryToken]
public class GympassCatalogController(AppDbContext db, IGympassService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string? search, int page = 1, CancellationToken ct = default)
    {
        var query = db.Gympasses.AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Passname.ToLower().Contains(search.ToLower()));
        var result = new CatalogListViewModel<PassFormViewModel> { Search = search, Total = await query.CountAsync(ct) };
        result.Page = Math.Clamp(page, 1, result.TotalPages);
        result.Items = await query.OrderBy(x => x.Passname).ThenBy(x => x.Passtypeid)
            .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize).Select(x => new PassFormViewModel { Id = x.Passtypeid, Name = x.Passname, Price = x.Price, MaxVisits = x.Maxvisits, ValidityDays = x.Validitydays, PurchasedCount = x.Customerpasses.Count }).ToListAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var result = await db.Gympasses.AsNoTracking().Where(x => x.Passtypeid == id).Select(x => new PassFormViewModel { Id = x.Passtypeid, Name = x.Passname, Price = x.Price, MaxVisits = x.Maxvisits, ValidityDays = x.Validitydays, PurchasedCount = x.Customerpasses.Count }).SingleOrDefaultAsync(ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public Task<IActionResult> Create(PassFormViewModel model, CancellationToken ct) { model.Id = null; return Save(model, ct); }

    [HttpPut("{id:int}")]
    public Task<IActionResult> Update(int id, PassFormViewModel model, CancellationToken ct) { model.Id = id; return Save(model, ct); }

    private async Task<IActionResult> Save(PassFormViewModel model, CancellationToken ct)
    {
        model.Name = model.Name.Trim();
        if (model.Name.Length == 0) ModelState.AddModelError(nameof(model.Name), "This field is required.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var existing = model.Id.HasValue ? await db.Gympasses.AsNoTracking().SingleOrDefaultAsync(x => x.Passtypeid == model.Id, ct) : null;
        if (model.Id.HasValue && existing == null) return NotFound();
        if (await db.Gympasses.AnyAsync(x => x.Passname == model.Name && x.Passtypeid != model.Id, ct))
            return Conflict(new { error = "An entry with this name already exists." });
        var dto = new GympassEditDTO { PassTypeId = model.Id, PassName = model.Name, Price = model.Price, MaxVisits = model.MaxVisits, ValidityDays = model.ValidityDays };
        if (existing != null)
        {
            if (existing.Passname == model.Name && existing.Price == model.Price && existing.Maxvisits == model.MaxVisits && existing.Validitydays == model.ValidityDays) return NoContent();
            return service.Update(dto) ? NoContent() : Conflict(new { error = "The entry could not be updated." });
        }
        var created = service.Create(dto);
        return created.HasValue ? CreatedAtAction(nameof(Get), new { id = created.Value }, new { id = created.Value }) : Conflict(new { error = "The entry could not be created." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (!await db.Gympasses.AnyAsync(x => x.Passtypeid == id, ct)) return NotFound();
        if (await db.Gympasses.AnyAsync(x => x.Passtypeid == id && x.Customerpasses.Any(), ct))
            return Conflict(new { error = "This entry is in use. Its history must be preserved." });
        return service.Delete(id) ? NoContent() : Conflict(new { error = "The entry could not be deleted." });
    }
}

