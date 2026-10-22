using System.ComponentModel.DataAnnotations;

namespace FitnessApp.DTO;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Username { get; set; }
    public DateOnly Dateofbirth { get; set; }
}

public record LoginRequest(
    [Required, EmailAddress, MaxLength(255)] string Email,
    [Required, MaxLength(128)] string Password);

public record AuthResult(
    bool IsSuccess,
    string? Error,
    int? Id,
    string? Email,
    string? Username)
{
    public static AuthResult Failure(string error) =>
        new(false, error, null, null, null);

    public static AuthResult Success(
        int id,
        string email,
        string username) =>
        new(true, null, id, email, username);
}