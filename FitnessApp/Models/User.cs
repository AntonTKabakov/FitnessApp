using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class User
{
    public int Id { get; set; }

    public string Fullname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateOnly? Dateofbirth { get; set; }

    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = UserRoles.User;

    public virtual ICollection<Customerpass> Customerpasses { get; set; }
        = new List<Customerpass>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; }
        = new List<RefreshToken>();
}
