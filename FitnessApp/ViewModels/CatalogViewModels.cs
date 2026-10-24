using System.ComponentModel.DataAnnotations;

namespace FitnessApp.ViewModels;

public sealed class CatalogListViewModel<T>
{
    public IReadOnlyList<T> Items { get; set; } = [];
    public string? Search { get; set; }
    public string? Error { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; } = 12;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));
}

public sealed class PassFormViewModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required, StringLength(50), Display(Name = "Pass name")]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "9999.99"), DataType(DataType.Currency)]
    public decimal Price { get; set; }

    [Range(1, int.MaxValue), Display(Name = "Maximum visits")]
    public int MaxVisits { get; set; } = 1;

    [Range(1, int.MaxValue), Display(Name = "Validity in days")]
    public int ValidityDays { get; set; } = 1;

    public int PurchasedCount { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (decimal.Round(Price, 2) != Price)
            yield return new ValidationResult("Enter a price with at most two decimal places.", [nameof(Price)]);
    }
}

public sealed class TrainerFormViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(100), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Specialty { get; set; } = string.Empty;

    [Required, StringLength(20), Phone, Display(Name = "Phone number")]
    public string Phone { get; set; } = string.Empty;

    public int CheckinCount { get; set; }
}
