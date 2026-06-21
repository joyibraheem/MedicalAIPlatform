using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
public sealed class MedicalReportController : Controller
{
    private readonly MedicalReportService _reports;
    private readonly MedicalReportPdfService _pdf;

    public MedicalReportController(MedicalReportService reports, MedicalReportPdfService pdf)
    {
        _reports = reports;
        _pdf = pdf;
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : View(dto);
    }

    public async Task<IActionResult> Print(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : View(dto);
    }

    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return NotFound();

        var bytes = _pdf.BuildPdf(dto);
        return File(bytes, "application/pdf", $"medical-report-{id:D}.pdf");
    }

    public async Task<IActionResult> LatestForPatient(int patientId, CancellationToken cancellationToken)
    {
        var rid = await _reports.GetLatestReportIdAsync(patientId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (rid is null)
        {
            TempData["MedicalReportMessage"] = "No structured medical report exists yet for this patient.";
            return RedirectToAction("Details", "Patient", new { id = patientId });
        }

        return RedirectToAction(nameof(Details), new { id = rid.Value });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int patientId, int? patientScanId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var id = await _reports.GenerateAndPersistAsync(patientId, userId, patientScanId, cancellationToken)
                .ConfigureAwait(false);
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["MedicalReportMessage"] = ex.Message;
            return RedirectToAction("Details", "Patient", new { id = patientId });
        }
    }
}
