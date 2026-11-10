using ClosedXML.Excel;
using FitnessApp.ViewModels;

namespace FitnessApp.Service;

public class ExcelReportService
{
    private const string MoneyFormat = "#,##0.00\" EUR\"";
    private const string DateFormat = "dd mmm yyyy";

    public byte[] Generate(StatsReportViewModel report)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = "FitnessApp Statistics";
        workbook.Properties.Author = "FitnessApp";
        workbook.Style.Font.FontName = "Calibri";
        workbook.Style.Font.FontSize = 11;

        var overview = Sheet(workbook, "Overview", "Metric", "Value", "Scope");
        var row = 2;
        void Metric(string name, XLCellValue value, string scope, string? format = null)
        {
            overview.Cell(row, 1).Value = name;
            overview.Cell(row, 2).Value = value;
            overview.Cell(row, 3).Value = scope;
            if (format != null) overview.Cell(row, 2).Style.NumberFormat.Format = format;
            row++;
        }
        Metric("Report start", report.Period.Start.ToDateTime(TimeOnly.MinValue), "Inclusive", DateFormat);
        Metric("Report end", report.Period.End.ToDateTime(TimeOnly.MinValue), "Inclusive", DateFormat);
        Metric("Generated", report.GeneratedAt, StatsPeriod.TimeZone, "dd mmm yyyy hh:mm:ss");
        Metric("Total customers", report.TotalCustomers, "All time; includes admin accounts");
        Metric("Active memberships", report.ActiveMemberships, "Current; expiry >= today and remaining visits > 0");
        Metric("Estimated revenue", report.AllTimeEstimatedRevenue, "All time", MoneyFormat);
        Metric("Total check-ins", report.TotalCheckins, "All time");
        Metric("Total trainers", report.TotalTrainers, "Current directory");
        Metric("Check-ins", report.PeriodCheckins, "Selected period");
        Metric("Estimated revenue", report.PeriodEstimatedRevenue, "Selected period", MoneyFormat);
        Metric("Membership purchases", report.PeriodPurchases, "Selected period");
        Metric("Purchasing customers", report.PeriodPurchasingCustomers, "Selected period; distinct accounts");
        Metric("Unassigned check-ins", report.UnassignedCheckins, "Selected period; no trainer");
        Metric("Revenue basis", StatsReportViewModel.RevenueNote, "Current prices; no historical payments");
        Metric("Customer counts", "Registration dates are not recorded.", "No new-customer metric can be calculated");

        var revenue = Sheet(workbook, "Revenue", "Month", "Pass type", "Purchases", "Estimated revenue (EUR)");
        row = 2;
        foreach (var month in report.MonthlyRevenue)
        {
            var details = report.RevenueBreakdown.Where(x => x.Year == month.Month.Year && x.Month == month.Month.Month).ToList();
            if (details.Count == 0)
            {
                revenue.Cell(row, 1).Value = month.Month.ToDateTime(TimeOnly.MinValue);
                revenue.Cell(row, 2).Value = "No purchases";
                revenue.Cell(row, 3).Value = 0;
                revenue.Cell(row++, 4).Value = 0m;
            }
            foreach (var item in details)
            {
                revenue.Cell(row, 1).Value = month.Month.ToDateTime(TimeOnly.MinValue);
                revenue.Cell(row, 2).Value = item.PassName;
                revenue.Cell(row, 3).Value = item.Purchases;
                revenue.Cell(row++, 4).Value = item.Revenue;
            }
        }
        revenue.Column(1).Style.NumberFormat.Format = "mmm yyyy";
        revenue.Column(4).Style.NumberFormat.Format = MoneyFormat;

        var attendance = Sheet(workbook, "Attendance", "Date", "Check-ins");
        row = 2;
        foreach (var item in report.Attendance)
        {
            attendance.Cell(row, 1).Value = item.Date.ToDateTime(TimeOnly.MinValue);
            attendance.Cell(row++, 2).Value = item.Checkins;
        }
        attendance.Column(1).Style.NumberFormat.Format = DateFormat;

        var memberships = Sheet(workbook, "Memberships", "Pass type", "Purchases", "Estimated revenue (EUR)");
        row = 2;
        foreach (var item in report.Memberships)
        {
            // SetValue stores text, including names beginning with '=', as literal strings, never formulas.
            memberships.Cell(row, 1).SetValue(item.PassName);
            memberships.Cell(row, 2).Value = item.Purchases;
            memberships.Cell(row++, 3).Value = item.Revenue;
        }
        memberships.Column(3).Style.NumberFormat.Format = MoneyFormat;

        var trainers = Sheet(workbook, "Trainers", "Trainer", "Specialty", "Check-ins");
        row = 2;
        foreach (var item in report.Trainers)
        {
            trainers.Cell(row, 1).SetValue(item.TrainerName);
            trainers.Cell(row, 2).SetValue(item.Specialty);
            trainers.Cell(row++, 3).Value = item.Checkins;
        }

        foreach (var sheet in workbook.Worksheets)
        {
            var used = sheet.RangeUsed()!;
            used.SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
            sheet.Columns(1, used.LastColumn().ColumnNumber()).AdjustToContents(12, 60);
            used.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            sheet.Row(1).Height = 28;
        }
        overview.Column(2).Width = 60;
        overview.Column(3).Width = 55;
        overview.RangeUsed()!.Style.Alignment.WrapText = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static IXLWorksheet Sheet(XLWorkbook workbook, string name, params string[] headers)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        var heading = sheet.Range(1, 1, 1, headers.Length);
        heading.Style.Font.Bold = true;
        heading.Style.Font.FontColor = XLColor.White;
        heading.Style.Fill.BackgroundColor = XLColor.FromHtml("#047857");
        return sheet;
    }
}
