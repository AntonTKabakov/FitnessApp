using FitnessApp.Data;
using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Controllers;

[Authorize(Roles = UserRoles.Admin)]
[TypeFilter(typeof(DatabaseUnavailableFilter))]
public class AdministrationController(AppDbContext db, CurrentUser currentUser, AccountRoleService roles) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        search = search?.Trim();
        if (search?.Length > 100) search = search[..100];
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Email.ToLower().Contains(search.ToLower()) || x.Fullname.ToLower().Contains(search.ToLower()));
        var pagination = new PaginationViewModel { TotalCount = await query.CountAsync(cancellationToken) };
        pagination.Page = Math.Clamp(page, 1, pagination.TotalPages);
        if (!string.IsNullOrWhiteSpace(search)) pagination.RouteValues["search"] = search;
        return View(new AccountRolesViewModel
        {
            CurrentUserId = currentUser.Id, Search = search, Pagination = pagination,
            Accounts = await query.OrderBy(x => x.Email).ThenBy(x => x.Id)
                .Skip((pagination.Page - 1) * pagination.PageSize).Take(pagination.PageSize)
                .Select(x => new AccountRoleRowViewModel { Id = x.Id, Name = x.Fullname, Email = x.Email, Role = x.Role }).ToListAsync(cancellationToken)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(ChangeRoleViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest();
        var error = await roles.ChangeAsync(currentUser.Id, model.Id, model.Role, cancellationToken);
        TempData[error == null ? "Success" : "Error"] = error ?? "Account role updated. The change takes effect on the next request.";
        return RedirectToAction(nameof(Index));
    }
}
