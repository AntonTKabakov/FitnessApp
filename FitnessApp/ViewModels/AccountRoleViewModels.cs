using System.ComponentModel.DataAnnotations;

namespace FitnessApp.ViewModels;

public class AccountRoleRowViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
}

public class ChangeRoleViewModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }
    [Required, RegularExpression("^(Admin|User)$")]
    public string Role { get; set; } = "";
}

public class AccountRolesViewModel
{
    public List<AccountRoleRowViewModel> Accounts { get; set; } = [];
    public PaginationViewModel Pagination { get; set; } = new();
    public string? Search { get; set; }
    public int CurrentUserId { get; set; }
}
