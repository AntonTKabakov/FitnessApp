namespace FitnessApp.DTO.CheckinDTOs;

public class CheckinResponseDTO
{
    public int CustomerPassId { get; set; }
    public DateTime CheckinDateTime { get; set; }
    public int? TrainerId { get; set; }
}