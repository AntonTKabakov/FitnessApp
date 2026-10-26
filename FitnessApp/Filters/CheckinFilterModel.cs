namespace FitnessApp.Filters;

public class CheckinFilterModel : BaseFilterModel
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int? OwnerId { get; set; }
    public int? CustomerPassId { get; set; }

    public int? TrainerId { get; set; }

    public DateTime? CheckinDateTime { get; set; } 
}
