using System;
using System.Collections.Generic;

namespace FitnessApp.Models;

public partial class Checkin
{
    public int Checkinid { get; set; }

    public int Customerpassid { get; set; }

    public DateTime Checkindatetime { get; set; }

    public int? Trainerid { get; set; }

    public virtual Customerpass Customerpass { get; set; } = null!;

    public virtual Trainer? Trainer { get; set; }
}
