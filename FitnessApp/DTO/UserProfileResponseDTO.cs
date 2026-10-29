namespace FitnessApp.DTO;

public record UserProfileResponseDTO(int Id, string FullName, string Email,
    DateOnly? DateOfBirth, string? ProfilePictureUrl);
