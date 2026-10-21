using System.ComponentModel.DataAnnotations;

namespace FitnessApp.DTO;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MinLength(8), MaxLength(128)] string Password,
    [MaxLength(50)] string? Username);

public record LoginRequest(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MaxLength(128)] string Password);