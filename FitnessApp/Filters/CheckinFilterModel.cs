namespace FitnessApp.Filters;

public class CheckinFilterModel : BaseFilterModel
{
    public int? CustomerPassId { get; set; }

    public int? TrainerId { get; set; }

    public DateTime? CheckinDateTime { get; set; } 
}