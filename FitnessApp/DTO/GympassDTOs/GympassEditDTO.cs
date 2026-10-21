namespace FitnessApp.DTO.GympassDTOs;

public class GympassEditDTO
{
    public int? PassTypeId { get; set; }
    public string PassName { get; set; } = null!;
    public decimal Price { get; set; }
    public int MaxVisits { get; set; }
    public int ValidityDays { get; set; }
}