using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
[ApiController]
[Route("api/medical-reports")]
public sealed class MedicalReportApiController : ControllerBase
{
    private readonly MedicalReportService _reports;

    public MedicalReportApiController(MedicalReportService reports)
    {
        _reports = reports;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<MedicalReportResponseDto>> GenerateAsync(
        [FromBody] MedicalReportGenerateRequestDto body, CancellationToken cancellationToken)
    {
        if (body.PatientId <= 0)
            return BadRequest(new { error = "patientId is required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var id = await _reports.GenerateAndPersistAsync(body.PatientId, userId, body.PatientScanId,
                    cancellationToken)
                .ConfigureAwait(false);
            var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
            return dto is null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MedicalReportResponseDto>> GetAsync(Guid id,
        CancellationToken cancellationToken)
    {
        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut("{id:guid}/clinical")]
    public async Task<ActionResult<MedicalReportResponseDto>> PutClinicalAsync(Guid id,
        [FromBody] MedicalReportClinicalEditDto body, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var ok = await _reports.ApplyDoctorEditAsync(id, userId, body, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return NotFound();

        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPost("{id:guid}/reset-ai")]
    public async Task<ActionResult<MedicalReportResponseDto>> ResetToAiAsync(Guid id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var ok = await _reports.RevertToAiAsync(id, userId, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return NotFound();

        var dto = await _reports.GetReportDtoAsync(id, cancellationToken).ConfigureAwait(false);
        return dto is null ? NotFound() : Ok(dto);
    }
}
