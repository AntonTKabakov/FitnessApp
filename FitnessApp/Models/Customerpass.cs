using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class Customerpass
{
    public int Customerpassid { get; set; }

    public int Customerid { get; set; }

    public int Passtypeid { get; set; }

    public DateOnly Purchasedate { get; set; }

    public DateOnly Expirydate { get; set; }

    public int Remainingvisits { get; set; }

    public virtual ICollection<Checkin> Checkins { get; set; } = new List<Checkin>();

    public virtual User Customer { get; set; } = null!;

    public virtual Gympass Passtype { get; set; } = null!;
}
