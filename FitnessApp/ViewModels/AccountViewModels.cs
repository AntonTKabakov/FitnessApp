using System.ComponentModel.DataAnnotations;

namespace FitnessApp.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress, StringLength(100)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel : IValidatableObject
{
    [Required, StringLength(50)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(100)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateOnly? DateOfBirth { get; set; }

    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, Compare(nameof(Password), ErrorMessage = "Passwords must match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var latestBirthDate = ReceptionTime.Today.AddYears(-14);
        if (DateOfBirth.HasValue && DateOfBirth.Value > latestBirthDate)
        {
            yield return new ValidationResult(
                "You must be at least 14 years old to register.",
                [nameof(DateOfBirth)]);
        }
    }
}
