using FitnessApp.ViewModels;

namespace FitnessApp.Service.Interfaces;

public interface IStatsService
{
    Task<StatsReportViewModel> GetReportAsync(StatsPeriod period, CancellationToken cancellationToken = default);
}
