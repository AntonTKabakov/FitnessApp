using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace FitnessApp.ViewModels;

public class PurchasePassViewModel
{
    [Required, Range(1, int.MaxValue), Display(Name = "Pass type")]
    public int? PassTypeId { get; set; }
    [ValidateNever] public int CustomerId { get; set; }
    [ValidateNever] public string CustomerName { get; set; } = "";
    [ValidateNever] public List<PurchasePassOptionViewModel> PassTypes { get; set; } = [];
}

public class PurchasePassOptionViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Visits { get; set; }
    public int ValidityDays { get; set; }
}
