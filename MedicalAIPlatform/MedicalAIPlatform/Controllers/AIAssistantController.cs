using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
public sealed class AIAssistantController : Controller
{
    private readonly AnalyticsJobQueueService _jobQueue;

    public AIAssistantController(AnalyticsJobQueueService jobQueue)
    {
        _jobQueue = jobQueue;
    }

    public IActionResult Index()
    {
        return View();
    }

    /// <summary>Queues an async assistant reply (same contract as <see cref="ChestAiChatApiController.EnqueueAsync"/>).</summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SendMessage([FromBody] AssistantChatEnqueueRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var jobId =
                await _jobQueue.EnqueueAssistantChatAsync(userId, request.Message, request.History ?? [],
                    request.PatientContextId, HttpContext.RequestAborted).ConfigureAwait(false);

            return Accepted(new { jobId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
