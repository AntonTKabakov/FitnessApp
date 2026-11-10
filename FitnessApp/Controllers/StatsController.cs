using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers;

[Authorize(Roles = UserRoles.Admin)]
[TypeFilter(typeof(DatabaseUnavailableFilter))]
[Route("Stats")]
public class StatsController(IStatsService stats, PdfReportService pdf, ExcelReportService excel) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        var model = new StatsViewModel { Filter = filter };
        if (ModelState.IsValid)
            model.Report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        else
            Response.StatusCode = StatusCodes.Status400BadRequest;
        return View(model);
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return PartialView("_Dashboard", await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken));
    }

    [HttpGet("ExportPdf")]
    public async Task<IActionResult> ExportPdf([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!pdf.IsAvailable) return Problem(statusCode: 503, title: "PDF export is not configured.",
            detail: "Configure Reporting:QuestPdfLicense after confirming QuestPDF license eligibility.");
        var report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return File(pdf.Generate(report), "application/pdf", Filename(report, "pdf"));
    }

    [HttpGet("ExportExcel")]
    public async Task<IActionResult> ExportExcel([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return File(excel.Generate(report), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Filename(report, "xlsx"));
    }

    private static string Filename(StatsReportViewModel report, string extension) =>
        $"FitnessApp-statistics-{report.Period.Start:yyyy-MM-dd}-to-{report.Period.End:yyyy-MM-dd}-generated-{report.GeneratedAt:yyyy-MM-dd}.{extension}";
}
