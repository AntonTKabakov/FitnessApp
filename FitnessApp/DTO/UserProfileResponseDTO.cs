namespace FitnessApp.DTO;

public record UserProfileResponseDTO(int Id, string FullName, string Email,
    DateOnly? DateOfBirth, string? ProfilePictureUrl)
{
    public string Role { get; init; } = Models.UserRoles.User;
}
