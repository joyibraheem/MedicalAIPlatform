using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
[ApiController]
[Route("api/job")]
public sealed class JobApiController : ControllerBase
{
    private const long MaxAnalyticsUploadBytes = 512L * 1024 * 1024;

    private readonly AnalyticsJobQueueService _jobQueue;
    private readonly ILogger<JobApiController> _logger;

    public JobApiController(AnalyticsJobQueueService jobQueue, ILogger<JobApiController> logger)
    {
        _jobQueue = jobQueue;
        _logger = logger;
    }

    /// <summary>Enqueue Chest CT / DICOM analysis; returns immediately with a job id for polling.</summary>
    [HttpPost("analyze-ct")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyticsUploadBytes)]
    [RequestSizeLimit(MaxAnalyticsUploadBytes)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnalyzeCtAsync(IFormFile ctFile, CancellationToken cancellationToken)
    {
        if (ctFile == null || ctFile.Length == 0)
            return BadRequest(new { error = "Please upload a CT scan file." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await using var ms = new MemoryStream(capacity: (int)Math.Min(ctFile.Length, int.MaxValue));
            await ctFile.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            var safeName = string.IsNullOrEmpty(ctFile.FileName) ? "(no name)" : Path.GetFileName(ctFile.FileName);
            var contentType = string.IsNullOrWhiteSpace(ctFile.ContentType)
                ? "application/octet-stream"
                : ctFile.ContentType.Trim();

            var jobId = await _jobQueue.StartCtJobAsync(userId, bytes, safeName, contentType, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new { jobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue CT analysis job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Enqueue CheXNet chest X-ray analysis; same job/polling contract as CT.</summary>
    [HttpPost("analyze-xray")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyticsUploadBytes)]
    [RequestSizeLimit(MaxAnalyticsUploadBytes)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AnalyzeXRayAsync(
        IFormFile xrayFile,
        string? xrayModel,
        CancellationToken cancellationToken)
    {
        if (xrayFile == null || xrayFile.Length == 0)
            return BadRequest(new { error = "Please upload a chest X-ray file." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            await using var ms = new MemoryStream(capacity: (int)Math.Min(xrayFile.Length, int.MaxValue));
            await xrayFile.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            var safeName = string.IsNullOrEmpty(xrayFile.FileName) ? "(no name)" : Path.GetFileName(xrayFile.FileName);
            var contentType = string.IsNullOrWhiteSpace(xrayFile.ContentType)
                ? "application/octet-stream"
                : xrayFile.ContentType.Trim();
            var modelId = ChestXRayModels.Normalize(xrayModel);

            var jobId = await _jobQueue
                .StartXRayJobAsync(userId, bytes, safeName, contentType, modelId, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new { jobId, xRayModelId = modelId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue X-ray analysis job");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("status/{jobId:guid}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GetStatus(Guid jobId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var dto = await _jobQueue.GetStatusForUserAsync(jobId, userId, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return NotFound(new { error = "Unknown or expired job id." });

        return Ok(dto);
    }
}
