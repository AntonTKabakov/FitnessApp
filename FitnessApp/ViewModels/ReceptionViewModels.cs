using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FitnessApp.ViewModels;

public static class ReceptionTime
{
    public static DateTime Now => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Sofia").DateTime;
    public static DateOnly Today => DateOnly.FromDateTime(Now);
}

public class PaginationViewModel
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalCount { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
    public Dictionary<string, string> RouteValues { get; set; } = new();
}

public class CustomerRowViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public int ActivePasses { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Email : FullName;
}

public class CustomersViewModel
{
    public string? Query { get; set; }
    public List<CustomerRowViewModel> Customers { get; set; } = [];
    public PaginationViewModel Pagination { get; set; } = new();
}

public class CustomerPassViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DateOnly Purchased { get; set; }
    public DateOnly Expires { get; set; }
    public int RemainingVisits { get; set; }
    public int MaxVisits { get; set; }
    public bool IsActive { get; set; }
    public bool CheckedInToday { get; set; }
    public string Status => IsActive ? "Active" : RemainingVisits <= 0 ? "No visits remaining" : "Expired";
}

public class CustomerDetailsViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    public List<CustomerPassViewModel> Passes { get; set; } = [];
    public List<VisitRowViewModel> RecentVisits { get; set; } = [];
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? Email : FullName;
}

public class VisitRowViewModel
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string PassName { get; set; } = "";
    public DateTime CheckedInAt { get; set; }
    public int CustomerPassId { get; set; }
    public int? TrainerId { get; set; }
    public string? TrainerName { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(CustomerName) ? CustomerEmail : CustomerName;
}

public class DashboardViewModel
{
    public DateOnly Today { get; set; }
    public int TodayVisits { get; set; }
    public int TrainerVisits { get; set; }
    public int ActivePasses { get; set; }
    public int Customers { get; set; }
    public List<VisitRowViewModel> RecentVisits { get; set; } = [];
}

public class VisitsViewModel
{
    public string? Query { get; set; }
    public DateOnly? Date { get; set; }
    public List<VisitRowViewModel> Visits { get; set; } = [];
    public PaginationViewModel Pagination { get; set; } = new();
}

public class CheckinFormViewModel
{
    [Required, Range(1, int.MaxValue), Display(Name = "Customer pass")]
    public int? CustomerPassId { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Trainer (optional)")]
    public int? TrainerId { get; set; }
    [ValidateNever] public string? Query { get; set; }
    [ValidateNever] public List<SelectListItem> Trainers { get; set; } = [];
    [ValidateNever] public List<CheckinPassOptionViewModel> Passes { get; set; } = [];
}

public class CheckinPassOptionViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly Expires { get; set; }
    public int RemainingVisits { get; set; }
    public bool CheckedInToday { get; set; }
}

public class VisitEditViewModel
{
    public int Id { get; set; }
    [Range(1, int.MaxValue), Display(Name = "Trainer (optional)")]
    public int? TrainerId { get; set; }
    [ValidateNever] public VisitRowViewModel Visit { get; set; } = new();
    [ValidateNever] public List<SelectListItem> Trainers { get; set; } = [];
}

public class UnavailableViewModel
{
    public string ReturnUrl { get; set; } = "/";
}
