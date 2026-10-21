using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class User
{
    public int Id { get; set; }

    public string Fullname { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string? Email { get; set; }

    public string Password { get; set; }

    public DateOnly Dateofbirth { get; set; }

    public virtual ICollection<Customerpass> Customerpasses { get; set; } = new List<Customerpass>();
}
