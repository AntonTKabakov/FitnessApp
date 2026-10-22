namespace FitnessApp.DTO.CheckinDTOs;

public class CheckinEditDTO
{
    public int? CheckinId { get; set; }
    public int CustomerPassId { get; set; }
    public DateTime CheckinDateTime { get; set; }
    public int? TrainerId { get; set; }
}