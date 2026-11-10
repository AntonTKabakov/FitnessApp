using System.Data;
using FitnessApp.Data;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Service;

public class StatsService(AppDbContext db, PdfReportService pdf) : IStatsService
{
    public async Task<StatsReportViewModel> GetReportAsync(StatsPeriod period, CancellationToken cancellationToken = default)
    {
        var now = ReceptionTime.Now;
        var today = DateOnly.FromDateTime(now);
        // Also defend callers outside MVC; a report can contain at most 366 daily rows.
        _ = new StatsFilterViewModel { Range = StatsRange.Custom, Start = period.Start, End = period.End }.Resolve(today);
        var start = period.StartTime;
        var end = period.EndExclusive;
        // All aggregates in a report see one consistent database snapshot.
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken);
        var purchases = db.Customerpasses.AsNoTracking().Where(x => x.Purchasedate >= period.Start && x.Purchasedate <= period.End);
        var checkins = db.Checkins.AsNoTracking().Where(x => x.Checkindatetime >= start && x.Checkindatetime < end);

        var totalCustomers = await db.Customers.CountAsync(cancellationToken);
        var activeMemberships = await db.Customerpasses.CountAsync(x => x.Expirydate >= today && x.Remainingvisits > 0, cancellationToken);
        var allTimeRevenue = await db.Customerpasses.SumAsync(x => (decimal?)x.Passtype.Price, cancellationToken) ?? 0m;
        var totalCheckins = await db.Checkins.CountAsync(cancellationToken);
        var totalTrainers = await db.Trainers.CountAsync(cancellationToken);
        var periodCheckins = await checkins.CountAsync(cancellationToken);
        var purchasingCustomers = await purchases.Select(x => x.Customerid).Distinct().CountAsync(cancellationToken);
        var unassignedCheckins = await checkins.CountAsync(x => x.Trainerid == null, cancellationToken);

        // Only aggregated rows leave the database, never full purchases, check-ins or accounts.
        var breakdown = await purchases.GroupBy(x => new { x.Purchasedate.Year, x.Purchasedate.Month, x.Passtypeid, x.Passtype.Passname })
            .Select(g => new RevenueBreakdownViewModel(g.Key.Year, g.Key.Month, g.Key.Passtypeid, g.Key.Passname, g.Count(), g.Sum(x => x.Passtype.Price)))
            .ToListAsync(cancellationToken);
        breakdown = breakdown.OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.PassName).ThenBy(x => x.PassTypeId).ToList();
        var memberships = breakdown.GroupBy(x => new { x.PassTypeId, x.PassName })
            .Select(g => new MembershipStatsViewModel(g.Key.PassTypeId, g.Key.PassName, g.Sum(x => x.Purchases), g.Sum(x => x.Revenue)))
            .OrderByDescending(x => x.Purchases).ThenBy(x => x.PassName).ThenBy(x => x.PassTypeId).ToList();
        var monthTotals = breakdown.GroupBy(x => new DateOnly(x.Year, x.Month, 1))
            .ToDictionary(g => g.Key, g => new MonthlyRevenueViewModel(g.Key, g.Sum(x => x.Purchases), g.Sum(x => x.Revenue)));
        var monthlyRevenue = new List<MonthlyRevenueViewModel>();
        var lastMonth = new DateOnly(period.End.Year, period.End.Month, 1);
        for (var month = new DateOnly(period.Start.Year, period.Start.Month, 1); month <= lastMonth; month = month.AddMonths(1))
            monthlyRevenue.Add(monthTotals.GetValueOrDefault(month) ?? new MonthlyRevenueViewModel(month, 0, 0m));

        var dayTotals = await checkins.GroupBy(x => x.Checkindatetime.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var dayLookup = dayTotals.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        var attendance = Enumerable.Range(0, period.End.DayNumber - period.Start.DayNumber + 1)
            .Select(offset => period.Start.AddDays(offset))
            .Select(day => new AttendanceViewModel(day, dayLookup.GetValueOrDefault(day))).ToList();
        var trainers = await db.Trainers.AsNoTracking().Select(x => new TrainerStatsViewModel(
                x.Trainerid, x.Fullname, x.Specialty,
                x.Checkins.Count(c => c.Checkindatetime >= start && c.Checkindatetime < end)))
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new StatsReportViewModel
        {
            Period = period, GeneratedAt = now, PdfAvailable = pdf.IsAvailable,
            TotalCustomers = totalCustomers, ActiveMemberships = activeMemberships,
            AllTimeEstimatedRevenue = allTimeRevenue, TotalCheckins = totalCheckins, TotalTrainers = totalTrainers,
            PeriodCheckins = periodCheckins, PeriodPurchasingCustomers = purchasingCustomers,
            PeriodPurchases = memberships.Sum(x => x.Purchases), PeriodEstimatedRevenue = memberships.Sum(x => x.Revenue),
            UnassignedCheckins = unassignedCheckins, MonthlyRevenue = monthlyRevenue, RevenueBreakdown = breakdown,
            Memberships = memberships, Attendance = attendance,
            Trainers = trainers.OrderByDescending(x => x.Checkins).ThenBy(x => x.TrainerName).ThenBy(x => x.TrainerId).ToList()
        };
    }
}
