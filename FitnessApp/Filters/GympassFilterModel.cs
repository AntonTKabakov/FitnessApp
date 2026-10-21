namespace FitnessApp.Filters;

public class GympassFilterModel : BaseFilterModel
{
    public string? PassName { get; set; }
    public decimal? Price { get; set; }
    public int? MaxVisits { get; set; }
    public int? ValidityDays { get; set; }
}