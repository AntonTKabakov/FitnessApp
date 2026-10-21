using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class Gympass
{
    public int Passtypeid { get; set; }

    public string Passname { get; set; } = null!;

    public decimal Price { get; set; }

    public int Maxvisits { get; set; }

    public int Validitydays { get; set; }

    public virtual ICollection<Customerpass> Customerpasses { get; set; } = new List<Customerpass>();
}
