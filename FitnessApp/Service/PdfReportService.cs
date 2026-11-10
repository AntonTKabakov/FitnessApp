using FitnessApp.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FitnessApp.Service;

public class PdfReportService
{
    public bool IsAvailable { get; }

    public PdfReportService(IConfiguration configuration)
    {
        var license = configuration["Reporting:QuestPdfLicense"];
        if (string.IsNullOrWhiteSpace(license)) return;
        // Eligibility is an explicit operator declaration, never inferred from deployment mode.
        QuestPDF.Settings.License = license switch
        {
            "Community" => LicenseType.Community,
            "Professional" => LicenseType.Professional,
            "Enterprise" => LicenseType.Enterprise,
            _ => throw new InvalidOperationException("Reporting:QuestPdfLicense must be Community, Professional, Enterprise, or empty.")
        };
        IsAvailable = true;
    }

    public byte[] Generate(StatsReportViewModel report)
    {
        if (!IsAvailable) throw new InvalidOperationException("QuestPDF license eligibility has not been configured.");
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(9).FontColor("#334155"));
            page.Header().PaddingBottom(16).Column(column =>
            {
                column.Item().Text("FitnessApp").FontSize(22).Bold().FontColor("#047857");
                column.Item().Text("ADMIN STATISTICS REPORT").FontSize(10).SemiBold();
                column.Item().PaddingTop(8).Text($"Reporting period: {report.Period.Label} (inclusive)");
                column.Item().Text($"Generated: {report.GeneratedAt:dd MMM yyyy HH:mm:ss} · {StatsPeriod.TimeZone}");
            });
            page.Content().Column(column =>
            {
                column.Spacing(14);
                column.Item().Background("#ecfdf5").Padding(10).Text(StatsReportViewModel.RevenueNote).FontSize(9);
                Section(column, "Overview", ["Metric", "Value", "Scope"],
                [
                    ["Total customers", report.TotalCustomers.ToString(), "All time; includes admin accounts"],
                    ["Active memberships", report.ActiveMemberships.ToString(), $"Current as of {report.GeneratedAt:dd MMM yyyy}"],
                    ["Estimated revenue", StatsReportViewModel.Money(report.AllTimeEstimatedRevenue), "All time"],
                    ["Total check-ins", report.TotalCheckins.ToString(), "All time"],
                    ["Total trainers", report.TotalTrainers.ToString(), "Current directory"],
                    ["Check-ins", report.PeriodCheckins.ToString(), "Selected period"],
                    ["Estimated revenue", StatsReportViewModel.Money(report.PeriodEstimatedRevenue), "Selected period"],
                    ["Membership purchases", report.PeriodPurchases.ToString(), "Selected period"],
                    ["Purchasing customers", report.PeriodPurchasingCustomers.ToString(), "Selected period; distinct accounts"],
                    ["Unassigned check-ins", report.UnassignedCheckins.ToString(), "Selected period; no trainer"]
                ]);
                column.Item().Text("Active means expiry date is today or later and remaining visits are greater than zero. Customer registration dates and historical payment amounts are not stored.").FontSize(8);
                Section(column, "Monthly estimated revenue · selected period", ["Month", "Purchases", "Estimated revenue (EUR)"],
                    report.MonthlyRevenue.Select(x => new[] { x.Month.ToString("MMM yyyy"), x.Purchases.ToString(), StatsReportViewModel.Money(x.Revenue) }));
                Section(column, "Popular memberships · selected period", ["Pass type", "Purchases", "Estimated revenue (EUR)"],
                    report.Memberships.Select(x => new[] { x.PassName, x.Purchases.ToString(), StatsReportViewModel.Money(x.Revenue) }));
                Section(column, "Trainer activity · selected period", ["Trainer", "Specialty", "Check-ins"],
                    report.Trainers.Select(x => new[] { x.TrainerName, x.Specialty, x.Checkins.ToString() }));
                column.Item().Text($"Check-ins without a trainer: {report.UnassignedCheckins}. These are included in attendance totals.").FontSize(8);
                Section(column, "Daily attendance · selected period", ["Date", "Check-ins"],
                    report.Attendance.Select(x => new[] { x.Date.ToString("dd MMM yyyy"), x.Checkins.ToString() }));
            });
            page.Footer().PaddingTop(12).AlignCenter().Text(text =>
            {
                text.Span("FitnessApp · ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static void Section(ColumnDescriptor column, string title, string[] headers, IEnumerable<string[]> rows)
    {
        column.Item().EnsureSpace(65).Column(section =>
        {
            section.Spacing(6);
            section.Item().Text(title).FontSize(12).SemiBold().FontColor("#0f172a");
            section.Item().Table(table =>
            {
                table.ColumnsDefinition(columns => { foreach (var _ in headers) columns.RelativeColumn(); });
                table.Header(header =>
                {
                    foreach (var label in headers)
                        header.Cell().Background("#047857").Padding(6).Text(label).FontColor(Colors.White).SemiBold();
                });
                var any = false;
                foreach (var row in rows)
                {
                    any = true;
                    foreach (var value in row)
                        table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6).Text(value);
                }
                if (!any)
                    table.Cell().ColumnSpan((uint)headers.Length).Padding(8).Text("No activity in the selected period.");
            });
        });
    }
}
