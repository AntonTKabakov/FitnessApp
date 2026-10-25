using System.Linq.Expressions;
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

// Read projections for server-rendered pages. Mutations continue through existing services.
public class ReceptionQueryService(AppDbContext db)
{
    private static readonly Expression<Func<Checkin, VisitRowViewModel>> VisitProjection = x => new VisitRowViewModel
    {
        Id = x.Checkinid, CustomerId = x.Customerpass.Customerid,
        CustomerName = x.Customerpass.Customer.Fullname, CustomerEmail = x.Customerpass.Customer.Email,
        CustomerPassId = x.Customerpassid, PassName = x.Customerpass.Passtype.Passname,
        CheckedInAt = x.Checkindatetime, TrainerId = x.Trainerid,
        TrainerName = x.Trainer == null ? null : x.Trainer.Fullname
    };

    public async Task<DashboardViewModel> Dashboard(CancellationToken ct)
    {
        var today = ReceptionTime.Today;
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        return new DashboardViewModel
        {
            Today = today,
            TodayVisits = await db.Checkins.CountAsync(x => x.Checkindatetime >= start && x.Checkindatetime < end, ct),
            TrainerVisits = await db.Checkins.CountAsync(x => x.Checkindatetime >= start && x.Checkindatetime < end && x.Trainerid != null, ct),
            ActivePasses = await db.Customerpasses.CountAsync(x => x.Expirydate >= today && x.Remainingvisits > 0, ct),
            Customers = await db.Customers.CountAsync(ct),
            RecentVisits = await db.Checkins.AsNoTracking().OrderByDescending(x => x.Checkindatetime).ThenByDescending(x => x.Checkinid)
                .Take(8).Select(VisitProjection).ToListAsync(ct)
        };
    }

    public async Task<CustomersViewModel> Customers(string? query, int page, CancellationToken ct)
    {
        query = NormalizeQuery(query);
        var today = ReceptionTime.Today;
        var source = db.Customers.AsNoTracking();
        if (query != null) source = source.Where(x => x.Fullname.ToLower().Contains(query.ToLower()) || x.Email.ToLower().Contains(query.ToLower()));
        var pagination = await Pagination(source, page, ct);
        if (query != null) pagination.RouteValues["query"] = query;
        return new CustomersViewModel
        {
            Query = query, Pagination = pagination,
            Customers = await source.OrderBy(x => x.Fullname).ThenBy(x => x.Id).Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize).Select(x => new CustomerRowViewModel
                {
                    Id = x.Id, FullName = x.Fullname, Email = x.Email,
                    ActivePasses = x.Customerpasses.Count(p => p.Expirydate >= today && p.Remainingvisits > 0)
                }).ToListAsync(ct)
        };
    }

    public async Task<CustomerDetailsViewModel?> Customer(int id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().Where(x => x.Id == id).Select(x => new CustomerDetailsViewModel
        {
            Id = x.Id, FullName = x.Fullname, Email = x.Email, DateOfBirth = x.Dateofbirth
        }).SingleOrDefaultAsync(ct);
        if (customer == null) return null;
        var today = ReceptionTime.Today;
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        customer.Passes = await db.Customerpasses.AsNoTracking().Where(x => x.Customerid == id)
            .OrderByDescending(x => x.Expirydate).ThenByDescending(x => x.Customerpassid).Select(x => new CustomerPassViewModel
            {
                Id = x.Customerpassid, Name = x.Passtype.Passname, Purchased = x.Purchasedate,
                Expires = x.Expirydate, RemainingVisits = x.Remainingvisits, MaxVisits = x.Passtype.Maxvisits,
                IsActive = x.Expirydate >= today && x.Remainingvisits > 0,
                CheckedInToday = x.Checkins.Any(v => v.Checkindatetime >= start && v.Checkindatetime < end)
            }).ToListAsync(ct);
        customer.RecentVisits = await db.Checkins.AsNoTracking().Where(x => x.Customerpass.Customerid == id)
            .OrderByDescending(x => x.Checkindatetime).ThenByDescending(x => x.Checkinid).Take(10).Select(VisitProjection).ToListAsync(ct);
        return customer;
    }

    public async Task<VisitsViewModel> Visits(string? query, DateOnly? date, int page, CancellationToken ct)
    {
        query = NormalizeQuery(query);
        var source = db.Checkins.AsNoTracking();
        if (query != null) source = source.Where(x => x.Customerpass.Customer.Fullname.ToLower().Contains(query.ToLower()) || x.Customerpass.Customer.Email.ToLower().Contains(query.ToLower()));
        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            source = source.Where(x => x.Checkindatetime.Date == start);
        }
        var pagination = await Pagination(source, page, ct);
        if (query != null) pagination.RouteValues["query"] = query;
        if (date.HasValue) pagination.RouteValues["date"] = date.Value.ToString("yyyy-MM-dd");
        return new VisitsViewModel
        {
            Query = query, Date = date, Pagination = pagination,
            Visits = await source.OrderByDescending(x => x.Checkindatetime).ThenByDescending(x => x.Checkinid)
                .Skip((pagination.Page - 1) * pagination.PageSize).Take(pagination.PageSize).Select(VisitProjection).ToListAsync(ct)
        };
    }

    public async Task<PurchasePassViewModel?> Purchase(int customerId, CancellationToken ct)
    {
        var model = await db.Customers.AsNoTracking().Where(x => x.Id == customerId)
            .Select(x => new PurchasePassViewModel { CustomerId = x.Id, CustomerName = x.Fullname == "" ? x.Email : x.Fullname })
            .SingleOrDefaultAsync(ct);
        if (model == null) return null;
        model.PassTypes = await db.Gympasses.AsNoTracking().OrderBy(x => x.Passname).ThenBy(x => x.Passtypeid)
            .Select(x => new PurchasePassOptionViewModel
            {
                Id = x.Passtypeid, Name = x.Passname, Price = x.Price, Visits = x.Maxvisits, ValidityDays = x.Validitydays
            }).ToListAsync(ct);
        return model;
    }

    public Task<VisitRowViewModel?> Visit(int id, CancellationToken ct) => db.Checkins.AsNoTracking()
        .Where(x => x.Checkinid == id).Select(VisitProjection).SingleOrDefaultAsync(ct);

    public Task<List<SelectListItem>> Trainers(CancellationToken ct) => db.Trainers.AsNoTracking().OrderBy(x => x.Fullname)
        .Select(x => new SelectListItem(x.Fullname + " · " + x.Specialty, x.Trainerid.ToString())).ToListAsync(ct);

    public async Task PopulateCheckin(CheckinFormViewModel model, int? customerId, CancellationToken ct)
    {
        var today = ReceptionTime.Today;
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);
        var query = NormalizeQuery(model.Query);
        var source = db.Customerpasses.AsNoTracking().Where(x => x.Expirydate >= today && x.Remainingvisits > 0);
        if (customerId.HasValue) source = source.Where(x => x.Customerid == customerId);
        if (query != null) source = source.Where(x => x.Customer.Fullname.ToLower().Contains(query.ToLower()) || x.Customer.Email.ToLower().Contains(query.ToLower()));
        // Preserve the chosen card when re-rendering a form, without loading the whole directory.
        if (model.CustomerPassId.HasValue) source = source.Where(x => x.Customerpassid == model.CustomerPassId);
        model.Passes = await source.OrderBy(x => x.Customer.Fullname).ThenBy(x => x.Customerpassid).Take(50)
            .Select(x => new CheckinPassOptionViewModel
            {
                Id = x.Customerpassid,
                CustomerName = x.Customer.Fullname == "" ? x.Customer.Email : x.Customer.Fullname,
                Name = x.Passtype.Passname, Expires = x.Expirydate, RemainingVisits = x.Remainingvisits,
                CheckedInToday = x.Checkins.Any(v => v.Checkindatetime >= start && v.Checkindatetime < end)
            }).ToListAsync(ct);
        model.Trainers = await Trainers(ct);
    }

    private static async Task<PaginationViewModel> Pagination<T>(IQueryable<T> source, int page, CancellationToken ct)
    {
        var pagination = new PaginationViewModel { TotalCount = await source.CountAsync(ct) };
        pagination.Page = Math.Clamp(page, 1, pagination.TotalPages);
        return pagination;
    }

    private static string? NormalizeQuery(string? query) => string.IsNullOrWhiteSpace(query) ? null : query.Trim()[..Math.Min(query.Trim().Length, 100)];
}
