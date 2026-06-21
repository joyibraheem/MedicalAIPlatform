using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

/// <summary>Human-in-the-loop prediction feedback API (no training performed here).</summary>
[ApiController]
[Route("api/feedback")]
[Authorize]
public sealed class PredictionFeedbackApiController : ControllerBase
{
    private readonly PredictionFeedbackService _feedback;
    private readonly UserManager<ApplicationUser> _users;

    public PredictionFeedbackApiController(PredictionFeedbackService feedback, UserManager<ApplicationUser> users)
    {
        _feedback = feedback;
        _users = users;
    }

    /// <summary>Doctor submits Accept / Modify for an analysis panel.</summary>
    [HttpPost("submit")]
    [Authorize(Policy = "VerifiedMedicalUser")]
    public async Task<IActionResult> Submit([FromBody] SubmitPredictionFeedbackDto dto, CancellationToken ct)
    {
        var user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
            return Unauthorized();

        var (ok, message, id) = await _feedback.SubmitAsync(user.Id, dto, ct).ConfigureAwait(false);
        if (!ok)
            return BadRequest(new { error = message });

        return Ok(new { message, id });
    }

    [HttpGet("pending")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Pending([FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var rows = await _feedback.GetPendingAsync(skip, take, ct).ConfigureAwait(false);
        return Ok(rows);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ReviewPredictionFeedbackDto? body, CancellationToken ct)
    {
        var admin = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (admin is null)
            return Unauthorized();

        var (ok, message) = await _feedback.ApproveAsync(id, admin.Id, body?.ReviewNotes, ct).ConfigureAwait(false);
        return ok ? Ok(new { message }) : BadRequest(new { error = message });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReviewPredictionFeedbackDto? body, CancellationToken ct)
    {
        var admin = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (admin is null)
            return Unauthorized();

        var (ok, message) = await _feedback.RejectAsync(id, admin.Id, body?.ReviewNotes, ct).ConfigureAwait(false);
        return ok ? Ok(new { message }) : BadRequest(new { error = message });
    }

    /// <summary>Export approved, not-yet-exported rows for offline training (JSON array).</summary>
    [HttpGet("export-approved")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> ExportApproved(CancellationToken ct)
    {
        var rows = await _feedback.GetApprovedNotExportedAsync(ct).ConfigureAwait(false);
        return Ok(rows);
    }

    /// <summary>Mark exported after offline pipeline ingests the batch.</summary>
    [HttpPost("mark-exported")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MarkExported([FromBody] MarkExportedDto dto, CancellationToken ct)
    {
        var admin = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (admin is null)
            return Unauthorized();

        var (ok, message) = await _feedback.MarkExportedAsync(admin.Id, dto, ct).ConfigureAwait(false);
        return ok ? Ok(new { message }) : BadRequest(new { error = message });
    }
}
