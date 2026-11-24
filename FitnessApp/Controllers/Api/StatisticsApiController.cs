using FitnessApp.Filters;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.Service.Interfaces;
using FitnessApp.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FitnessApp.Controllers.Api;

[Authorize(Roles = UserRoles.Admin)]
[ApiController]
[Route("api/statistics")]
public class StatisticsApiController(IStatsService stats, ReportExportService exports) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        var model = new StatsViewModel { Filter = filter };
        if (ModelState.IsValid)
            model.Report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        else
            Response.StatusCode = StatusCodes.Status400BadRequest;
        return ModelState.IsValid ? Ok(model) : ValidationProblem(ModelState);
    }

    [HttpGet("Data")]
    public async Task<IActionResult> Data([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        return Ok( await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken));
    }

    [HttpGet("ExportPdf")]
    public async Task<IActionResult> ExportPdf([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!exports.IsAvailable("pdf")) return Problem(statusCode: 503, title: "PDF export is not configured.",
            detail: "Configure Reporting:QuestPdfLicense after confirming QuestPDF license eligibility.");
        var report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var export = exports.Generate("pdf", report);
        return File(export.Content, export.ContentType, export.Filename);
    }

    [HttpGet("ExportExcel")]
    public async Task<IActionResult> ExportExcel([FromQuery] StatsFilterViewModel filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var report = await stats.GetReportAsync(filter.Resolve(ReceptionTime.Today), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var export = exports.Generate("excel", report);
        return File(export.Content, export.ContentType, export.Filename);
    }

}
