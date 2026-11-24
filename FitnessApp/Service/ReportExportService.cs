using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;

namespace FitnessApp.Service;

public sealed record ReportExport(byte[] Content, string ContentType, string Filename);

public class ReportExportService(IEnumerable<IReportExportStrategy> strategies)
{
    private readonly IReadOnlyDictionary<string, IReportExportStrategy> formats =
        strategies.ToDictionary(x => x.Format, StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable(string format) => formats.TryGetValue(format, out var strategy) && strategy.IsAvailable;

    public ReportExport Generate(string format, StatsReportViewModel report)
    {
        if (!formats.TryGetValue(format, out var strategy) || !strategy.IsAvailable)
            throw new InvalidOperationException("The requested report format is unavailable.");
        var filename = $"FitnessApp-statistics-{report.Period.Start:yyyy-MM-dd}-to-{report.Period.End:yyyy-MM-dd}-generated-{report.GeneratedAt:yyyy-MM-dd}.{strategy.Extension}";
        return new ReportExport(strategy.Generate(report), strategy.ContentType, filename);
    }
}
