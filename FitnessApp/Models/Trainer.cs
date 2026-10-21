using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class Trainer
{
    public int Trainerid { get; set; }

    public string Fullname { get; set; } = null!;

    public string Specialty { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public virtual ICollection<Checkin> Checkins { get; set; } = new List<Checkin>();
}
