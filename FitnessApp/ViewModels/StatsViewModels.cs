using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace FitnessApp.ViewModels;

public enum StatsRange { Today, Last7Days, Last30Days, ThisMonth, ThisYear, Custom }

public class StatsFilterViewModel : IValidatableObject
{
    public const int MaximumDays = 366;
    public StatsRange Range { get; set; } = StatsRange.Last30Days;
    [DataType(DataType.Date)] public DateOnly? Start { get; set; }
    [DataType(DataType.Date)] public DateOnly? End { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Range))
            yield return new ValidationResult("Choose a supported reporting period.", [nameof(Range)]);
        if (Range != StatsRange.Custom) yield break;
        if (!Start.HasValue || !End.HasValue)
            yield return new ValidationResult("Enter both a start date and an end date.", [nameof(Start), nameof(End)]);
        else if (Start > End)
            yield return new ValidationResult("The start date must be on or before the end date.", [nameof(Start), nameof(End)]);
        else if (End > ReceptionTime.Today)
            yield return new ValidationResult("Reporting dates cannot be in the future (Europe/Sofia).", [nameof(End)]);
        else if (End.Value.DayNumber - Start.Value.DayNumber + 1 > MaximumDays)
            yield return new ValidationResult($"Choose a reporting period of {MaximumDays} days or fewer.", [nameof(Start), nameof(End)]);
    }

    public StatsPeriod Resolve(DateOnly today)
    {
        var start = Range switch
        {
            StatsRange.Today => today,
            StatsRange.Last7Days => today.AddDays(-6),
            StatsRange.Last30Days => today.AddDays(-29),
            StatsRange.ThisMonth => new DateOnly(today.Year, today.Month, 1),
            StatsRange.ThisYear => new DateOnly(today.Year, 1, 1),
            StatsRange.Custom when Start.HasValue && End.HasValue => Start.Value,
            _ => throw new ValidationException("Invalid reporting period.")
        };
        var end = Range == StatsRange.Custom ? End!.Value : today;
        if (start > end || end > today || end.DayNumber - start.DayNumber + 1 > MaximumDays)
            throw new ValidationException("Invalid reporting dates. Select up to 366 days, ending no later than today.");
        return new StatsPeriod(start, end);
    }
}

public sealed record StatsPeriod(DateOnly Start, DateOnly End)
{
    public const string TimeZone = "Europe/Sofia";
    public string Label => $"{Start:dd MMM yyyy} – {End:dd MMM yyyy}";
    // Existing check-ins use Sofia wall-clock time in PostgreSQL timestamp without time zone.
    public DateTime StartTime => Start.ToDateTime(TimeOnly.MinValue);
    public DateTime EndExclusive => End.AddDays(1).ToDateTime(TimeOnly.MinValue);
}

public class StatsViewModel
{
    public StatsFilterViewModel Filter { get; set; } = new();
    public StatsReportViewModel? Report { get; set; }
}

public class StatsReportViewModel
{
    public const string RevenueNote = "Estimated revenue uses current gym pass prices. Historical prices paid and payment transactions are not recorded; this is not an accounting revenue total.";
    public required StatsPeriod Period { get; init; }
    public DateTime GeneratedAt { get; init; }
    public bool PdfAvailable { get; init; }
    public int TotalCustomers { get; init; }
    public int ActiveMemberships { get; init; }
    public decimal AllTimeEstimatedRevenue { get; init; }
    public int TotalCheckins { get; init; }
    public int TotalTrainers { get; init; }
    public int PeriodCheckins { get; init; }
    public int PeriodPurchases { get; init; }
    public int PeriodPurchasingCustomers { get; init; }
    public decimal PeriodEstimatedRevenue { get; init; }
    public int UnassignedCheckins { get; init; }
    public IReadOnlyList<MonthlyRevenueViewModel> MonthlyRevenue { get; init; } = [];
    public IReadOnlyList<RevenueBreakdownViewModel> RevenueBreakdown { get; init; } = [];
    public IReadOnlyList<AttendanceViewModel> Attendance { get; init; } = [];
    public IReadOnlyList<MembershipStatsViewModel> Memberships { get; init; } = [];
    public IReadOnlyList<TrainerStatsViewModel> Trainers { get; init; } = [];
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture) + " EUR";
}

public sealed record MonthlyRevenueViewModel(DateOnly Month, int Purchases, decimal Revenue);
public sealed record RevenueBreakdownViewModel(int Year, int Month, int PassTypeId, string PassName, int Purchases, decimal Revenue);
public sealed record AttendanceViewModel(DateOnly Date, int Checkins);
public sealed record MembershipStatsViewModel(int PassTypeId, string PassName, int Purchases, decimal Revenue);
public sealed record TrainerStatsViewModel(int TrainerId, string TrainerName, string Specialty, int Checkins);
