using System.Security.Claims;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
[ApiController]
[Route("api/assistant")]
public sealed class ChestAiChatApiController : ControllerBase
{
    private readonly AnalyticsJobQueueService _jobs;
    private readonly ApplicationDbContext _db;

    public ChestAiChatApiController(AnalyticsJobQueueService jobs, ApplicationDbContext db)
    {
        _jobs = jobs;
        _db = db;
    }

    /// <summary>Conversation history for the signed-in user (MedAI / ChestAI assistant).</summary>
    [HttpGet("chat/messages")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GetMessagesAsync([FromQuery] int take = 150, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 400);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var rows = await _db.ChestAiChatMessages.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        rows.Reverse();

        var dto = rows.Select(m => new ChestAiChatMessageDto
        {
            Role = m.Role,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            RelatedJobId = m.RelatedJobId,
            MetadataJson = m.MetadataJson
        }).ToList();

        return Ok(new { messages = dto });
    }

    /// <summary>Enqueue async assistant reply (survives navigation). Completion via SignalR <c>jobUpdate</c> or GET /api/job/status/{{jobId}}.</summary>
    [HttpPost("chat/enqueue")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> EnqueueAsync([FromBody] AssistantChatEnqueueRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var jobId = await _jobs.EnqueueAssistantChatAsync(userId, request.Message, request.History ?? [],
                request.PatientContextId, cancellationToken).ConfigureAwait(false);

            return Accepted(new { jobId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>Poll assistant job status (same schema as CT jobs).</summary>
    [HttpGet("chat/job/{jobId:guid}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> GetAssistantJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var dto = await _jobs.GetStatusForUserAsync(jobId, userId, cancellationToken).ConfigureAwait(false);
        if (dto is null || !string.Equals(dto.Kind, ChestAiBackgroundJob.KindAssistantChat, StringComparison.Ordinal))
            return NotFound(new { error = "Unknown job." });

        return Ok(dto);
    }
}
