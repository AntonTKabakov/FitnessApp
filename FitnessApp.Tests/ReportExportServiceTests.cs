using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.Extensions.Configuration;

namespace FitnessApp.Tests;

public class ReportExportServiceTests
{
    private static StatsReportViewModel Report => new()
    {
        Period = new StatsPeriod(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 2)),
        GeneratedAt = new DateTime(2020, 1, 3)
    };

    [Fact]
    public void New_format_can_be_selected_without_changing_export_service()
    {
        var service = new ReportExportService([new TextExport()]);
        var result = service.Generate("TEXT", Report);
        Assert.True(service.IsAvailable("text"));
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(new byte[] { 42 }, result.Content);
        Assert.Equal("FitnessApp-statistics-2020-01-01-to-2020-01-02-generated-2020-01-03.txt", result.Filename);
    }

    [Fact]
    public void Unavailable_and_unknown_formats_are_rejected()
    {
        var service = new ReportExportService([new PdfReportService(new ConfigurationBuilder().Build()), new ExcelReportService()]);
        Assert.False(service.IsAvailable("pdf"));
        Assert.False(service.IsAvailable("unknown"));
        Assert.True(service.IsAvailable("excel"));
        Assert.Throws<InvalidOperationException>(() => service.Generate("pdf", Report));
        Assert.Throws<InvalidOperationException>(() => service.Generate("unknown", Report));
        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(service.Generate("excel", Report).Content));
        Assert.Equal(5, workbook.Worksheets.Count);
    }

    private sealed class TextExport : IReportExportStrategy
    {
        public string Format => "text";
        public string ContentType => "text/plain";
        public string Extension => "txt";
        public bool IsAvailable => true;
        public byte[] Generate(StatsReportViewModel report) => [42];
    }
}
