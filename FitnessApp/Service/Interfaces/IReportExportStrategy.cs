using FitnessApp.ViewModels;

namespace FitnessApp.Service.Interfaces;

public interface IReportExportStrategy
{
    string Format { get; }
    string ContentType { get; }
    string Extension { get; }
    bool IsAvailable { get; }
    byte[] Generate(StatsReportViewModel report);
}
