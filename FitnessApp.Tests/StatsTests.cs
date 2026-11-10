using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FitnessApp.Data;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FitnessApp.Tests;

public sealed class StatsTests
{
    private static readonly StatsPeriod Period = new(new DateOnly(2025, 9, 28), new DateOnly(2025, 10, 2));

    [Theory]
    [InlineData(StatsRange.Today, "2024-03-01")]
    [InlineData(StatsRange.Last7Days, "2024-02-24")]
    [InlineData(StatsRange.Last30Days, "2024-02-01")]
    [InlineData(StatsRange.ThisMonth, "2024-03-01")]
    [InlineData(StatsRange.ThisYear, "2024-01-01")]
    public void Presets_include_today_and_handle_leap_years(StatsRange range, string expectedStart)
    {
        var today = new DateOnly(2024, 3, 1);
        var period = new StatsFilterViewModel { Range = range }.Resolve(today);
        Assert.Equal(DateOnly.Parse(expectedStart), period.Start);
        Assert.Equal(today, period.End);
        Assert.Equal(DateTimeKind.Unspecified, period.StartTime.Kind);
        Assert.Equal(today.AddDays(1).ToDateTime(TimeOnly.MinValue), period.EndExclusive);
        Assert.Equal(StatsRange.Last30Days, new StatsFilterViewModel().Range);
    }

    [Fact]
    public void Custom_dates_validate_missing_reversed_future_and_overlarge_periods()
    {
        var today = ReceptionTime.Today;
        var filters = new[]
        {
            new StatsFilterViewModel { Range = (StatsRange)999 },
            new StatsFilterViewModel { Range = StatsRange.Custom },
            new StatsFilterViewModel { Range = StatsRange.Custom, Start = today },
            new StatsFilterViewModel { Range = StatsRange.Custom, Start = today, End = today.AddDays(-1) },
            new StatsFilterViewModel { Range = StatsRange.Custom, Start = today, End = today.AddDays(1) },
            new StatsFilterViewModel { Range = StatsRange.Custom, Start = today.AddDays(-366), End = today }
        };
        foreach (var filter in filters)
            Assert.False(Validator.TryValidateObject(filter, new ValidationContext(filter), [], true));
        foreach (var days in new[] { 1, 366 })
        {
            var filter = new StatsFilterViewModel { Range = StatsRange.Custom, Start = today.AddDays(1 - days), End = today };
            Assert.True(Validator.TryValidateObject(filter, new ValidationContext(filter), [], true));
            Assert.Equal(days, filter.Resolve(today).End.DayNumber - filter.Resolve(today).Start.DayNumber + 1);
        }
    }

    [Theory]
    [InlineData("/Stats")]
    [InlineData("/Stats/Data")]
    [InlineData("/Stats/ExportPdf")]
    [InlineData("/Stats/ExportExcel")]
    public async Task Every_endpoint_requires_a_current_admin_role(string route)
    {
        await using var app = new FitnessAppFactory();
        using var anonymous = app.NewClient();
        var denied = await anonymous.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/Account/Login", denied.Headers.Location!.OriginalString);
        using var member = await app.RegisterAsync();
        denied = await member.Client.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location!.OriginalString);
        using var admin = await app.RegisterAsync(UserRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(route, TestContext.Current.CancellationToken)).StatusCode);
        await app.WithDatabaseAsync(async db =>
        {
            (await db.Users.SingleAsync(x => x.Id == admin.Id)).Role = UserRoles.User;
            await db.SaveChangesAsync();
        });
        denied = await admin.Client.GetAsync(route, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/Account/AccessDenied", denied.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("Range=Custom")]
    [InlineData("Range=Unknown")]
    [InlineData("Range=999")]
    [InlineData("Range=Custom&Start=bad&End=2025-01-01")]
    [InlineData("Range=Custom&Start=2025-02-01&End=2025-01-01")]
    [InlineData("Range=Custom&Start=2020-01-01&End=2025-01-01")]
    [InlineData("Range=Custom&Start=9999-12-31&End=9999-12-31")]
    public async Task Invalid_filters_are_rejected_on_dashboard_data_and_exports(string query)
    {
        await using var app = new FitnessAppFactory();
        using var admin = await app.RegisterAsync(UserRoles.Admin);
        foreach (var route in new[] { "/Stats", "/Stats/Data", "/Stats/ExportPdf", "/Stats/ExportExcel" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.Client.GetAsync(route + "?" + query, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Empty_database_zero_fills_attendance_and_generates_both_reports()
    {
        await using var app = new FitnessAppFactory();
        using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IStatsService>().GetReportAsync(Period, TestContext.Current.CancellationToken);
        Assert.Equal(0, report.TotalCustomers);
        Assert.Equal(0m, report.AllTimeEstimatedRevenue);
        Assert.Equal(0, report.ActiveMemberships);
        Assert.Equal(5, report.Attendance.Count);
        Assert.All(report.Attendance, x => Assert.Equal(0, x.Checkins));
        Assert.Equal(2, report.MonthlyRevenue.Count);
        Assert.Empty(report.Memberships);
        Assert.Empty(report.Trainers);
        VerifyPdf(scope.ServiceProvider.GetRequiredService<PdfReportService>().Generate(report));
        using var workbook = new XLWorkbook(new MemoryStream(scope.ServiceProvider.GetRequiredService<ExcelReportService>().Generate(report)));
        Assert.Equal(5, workbook.Worksheets.Count);
        Assert.Equal(0d, workbook.Worksheet("Overview").Cell(5, 2).GetDouble());
        Assert.Equal(6, workbook.Worksheet("Attendance").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Queries_and_export_values_match_inclusive_boundaries_and_real_business_rules()
    {
        await using var app = new FitnessAppFactory();
        await app.WithDatabaseAsync(SeedAsync);
        using var scope = app.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IStatsService>().GetReportAsync(Period, TestContext.Current.CancellationToken);
        VerifyReport(report);
        var excel = scope.ServiceProvider.GetRequiredService<ExcelReportService>().Generate(report);
        using var workbook = new XLWorkbook(new MemoryStream(excel));
        Assert.Equal(new[] { "Overview", "Revenue", "Attendance", "Memberships", "Trainers" }, workbook.Worksheets.Select(x => x.Name));
        Assert.Equal(100d, workbook.Worksheet("Revenue").RowsUsed().Skip(1).Sum(x => x.Cell(4).GetDouble()));
        Assert.Equal(3d, workbook.Worksheet("Attendance").RowsUsed().Skip(1).Sum(x => x.Cell(2).GetDouble()));
        Assert.Equal(100d, workbook.Worksheet("Memberships").RowsUsed().Skip(1).Sum(x => x.Cell(3).GetDouble()));
        Assert.Equal(2d, workbook.Worksheet("Trainers").RowsUsed().Skip(1).Sum(x => x.Cell(3).GetDouble()));
        Assert.Equal(XLDataType.DateTime, workbook.Worksheet("Attendance").Cell(2, 1).DataType);
        Assert.Equal(XLDataType.Number, workbook.Worksheet("Revenue").Cell(2, 4).DataType);
        Assert.Contains("EUR", workbook.Worksheet("Revenue").Cell(2, 4).Style.NumberFormat.Format);
        Assert.False(workbook.Worksheet("Memberships").Cell(2, 1).HasFormula);
        Assert.All(workbook.Worksheets, x =>
        {
            Assert.True(x.Cell(1, 1).Style.Font.Bold);
            Assert.True(x.AutoFilter.IsEnabled);
            Assert.Equal(1, x.SheetView.SplitRow);
        });
        VerifyPdf(scope.ServiceProvider.GetRequiredService<PdfReportService>().Generate(report));
    }

    [Fact]
    public async Task Dashboard_and_exports_preserve_the_selected_period_and_encode_names()
    {
        await using var app = new FitnessAppFactory();
        using var admin = await app.RegisterAsync(UserRoles.Admin);
        await app.WithDatabaseAsync(SeedAsync);
        const string query = "?Range=Custom&Start=2025-09-28&End=2025-10-02";
        var page = await admin.Client.GetAsync("/Stats" + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("/Stats/ExportPdf?range=Custom&amp;start=2025-09-28&amp;end=2025-10-02", html);
        Assert.Contains("/Stats/ExportExcel?range=Custom&amp;start=2025-09-28&amp;end=2025-10-02", html);
        Assert.DoesNotContain("<script>alert('pass')</script>", html);
        Assert.Contains("Estimated revenue", html);
        var partial = await admin.Client.GetStringAsync("/Stats/Data" + query, TestContext.Current.CancellationToken);
        var json = Regex.Match(partial, "<script type=\"application/json\" data-stats-report>([\\s\\S]*?)</script>").Groups[1].Value;
        using var data = JsonDocument.Parse(json);
        Assert.Equal(100m, data.RootElement.GetProperty("periodEstimatedRevenue").GetDecimal());
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        foreach (var (route, mime) in new[] { ("ExportPdf", "application/pdf"), ("ExportExcel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") })
        {
            var response = await admin.Client.GetAsync("/Stats/" + route + query, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(mime, response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("2025-09-28-to-2025-10-02", response.Content.Headers.ContentDisposition!.FileName);
            var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            if (route == "ExportPdf") VerifyPdf(bytes);
            else
            {
                using var workbook = new XLWorkbook(new MemoryStream(bytes));
                Assert.Equal(100d, workbook.Worksheet("Revenue").RowsUsed().Skip(1).Sum(x => x.Cell(4).GetDouble()));
            }
        }
        // Existing admin and member pages still render with the shared navigation changes.
        foreach (var route in new[] { "/Home", "/Customers", "/Passes", "/Team", "/Visits", "/Administration", "/Member" })
            Assert.Equal(HttpStatusCode.OK, (await admin.Client.GetAsync(route, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Large_attendance_report_generates_a_multipage_pdf()
    {
        await using var app = new FitnessAppFactory();
        using var scope = app.Services.CreateScope();
        var today = ReceptionTime.Today;
        var report = await scope.ServiceProvider.GetRequiredService<IStatsService>().GetReportAsync(new StatsPeriod(today.AddDays(-365), today), TestContext.Current.CancellationToken);
        Assert.Equal(366, report.Attendance.Count);
        VerifyPdf(scope.ServiceProvider.GetRequiredService<PdfReportService>().Generate(report));
    }

    [Fact]
    public void Pdf_export_requires_an_explicit_license_declaration()
    {
        var service = new PdfReportService(new ConfigurationBuilder().Build());
        Assert.False(service.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => service.Generate(new StatsReportViewModel { Period = Period }));
        Assert.Throws<InvalidOperationException>(() => new PdfReportService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reporting:QuestPdfLicense"] = "invalid" }).Build()));
    }

    [Fact]
    public async Task PostgreSql_executes_statistics_queries_against_an_isolated_database()
    {
        var connection = Environment.GetEnvironmentVariable("FITNESS_STATS_TEST_POSTGRES");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(connection), "Set FITNESS_STATS_TEST_POSTGRES to a disposable PostgreSQL server to run the provider integration test.");
        var settings = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Database = "fitness_stats_test_" + Guid.NewGuid().ToString("N"), Pooling = false };
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(settings.ConnectionString).Options);
        try
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            await SeedAsync(db);
            var pdf = new PdfReportService(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Reporting:QuestPdfLicense"] = "Community" }).Build());
            VerifyReport(await new StatsService(db, pdf).GetReportAsync(Period, TestContext.Current.CancellationToken));
        }
        finally
        {
            // The generated database name is never the configured/default application database.
            await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        }
    }

    private static void VerifyPdf(byte[] bytes)
    {
        Assert.True(bytes.Length > 1000);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes[^32..]));
    }

    private static void VerifyReport(StatsReportViewModel report)
    {
        Assert.Equal(200m, report.AllTimeEstimatedRevenue);
        Assert.Equal(100m, report.PeriodEstimatedRevenue);
        Assert.Equal(2, report.ActiveMemberships);
        Assert.Equal(5, report.TotalCheckins);
        Assert.Equal(3, report.PeriodCheckins);
        Assert.Equal(2, report.TotalTrainers);
        Assert.Equal(2, report.PeriodPurchases);
        Assert.Equal(1, report.PeriodPurchasingCustomers);
        Assert.Equal(1, report.UnassignedCheckins);
        Assert.Equal(new[] { 30m, 70m }, report.MonthlyRevenue.Select(x => x.Revenue));
        Assert.Equal(new[] { 1, 0, 0, 0, 2 }, report.Attendance.Select(x => x.Checkins));
        Assert.Equal(new[] { 2, 0 }, report.Trainers.Select(x => x.Checkins));
        Assert.Equal(2, report.Memberships.Count);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        var today = ReceptionTime.Today;
        var user = new User { Fullname = "Statistics member", Email = "statistics@example.test", Password = "not-a-real-password" };
        var basic = new Gympass { Passname = "=Basic <script>alert('pass')</script>", Price = 30m, Maxvisits = 10, Validitydays = 30 };
        var premium = new Gympass { Passname = "Premium", Price = 70m, Maxvisits = 20, Validitydays = 60 };
        var busy = new Trainer { Fullname = "Active trainer", Specialty = "Strength", Phone = "000" };
        var idle = new Trainer { Fullname = "Idle trainer", Specialty = "Mobility", Phone = "001" };
        var before = new Customerpass { Customer = user, Passtype = basic, Purchasedate = Period.Start.AddDays(-1), Expirydate = today.AddDays(-1), Remainingvisits = 5 };
        var first = new Customerpass { Customer = user, Passtype = basic, Purchasedate = Period.Start, Expirydate = today, Remainingvisits = 5 };
        var last = new Customerpass { Customer = user, Passtype = premium, Purchasedate = Period.End, Expirydate = today.AddDays(1), Remainingvisits = 0 };
        var after = new Customerpass { Customer = user, Passtype = premium, Purchasedate = Period.End.AddDays(1), Expirydate = today.AddDays(1), Remainingvisits = 2 };
        db.AddRange(before, first, last, after, idle);
        db.Checkins.AddRange(
            new Checkin { Customerpass = before, Checkindatetime = Period.StartTime.AddTicks(-1) },
            new Checkin { Customerpass = first, Checkindatetime = Period.StartTime, Trainer = busy },
            new Checkin { Customerpass = last, Checkindatetime = Period.EndExclusive.AddHours(-12) },
            new Checkin { Customerpass = last, Checkindatetime = Period.EndExclusive.AddTicks(-10), Trainer = busy },
            new Checkin { Customerpass = after, Checkindatetime = Period.EndExclusive, Trainer = busy });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
