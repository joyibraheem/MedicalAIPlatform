namespace MedicalAIPlatform.Models;

using System.ComponentModel.DataAnnotations.Schema;

/// <summary>Persistent background job (CT / chest X-ray analysis or async assistant reply).</summary>
public sealed class ChestAiBackgroundJob
{
    public const string KindCt = "ct";
    public const string KindXRay = "xray";
    public const string KindAssistantChat = "assistant_chat";

    public const string StatusQueued = "queued";
    public const string StatusProcessing = "processing";
    public const string StatusDone = "done";
    public const string StatusFailed = "failed";

    public Guid Id { get; set; }

    /// <summary>AspNetUsers.Id</summary>
    public string UserId { get; set; } = "";

    public string Kind { get; set; } = KindCt;

    public string Status { get; set; } = StatusQueued;

    /// <summary>JSON: imaging temp path + file meta (CT or X-ray), or assistant chat payload.</summary>
    public string? InputPayloadJson { get; set; }

    /// <summary>JSON: <see cref="AnalyticsCtJobResultDto"/> (CT or CheXNet jobs) or assistant summary.</summary>
    public string? ResultPayloadJson { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Imaging study this job processed (CT / X-ray jobs only).</summary>
    public int? PatientScanId { get; set; }

    [ForeignKey(nameof(PatientScanId))]
    public PatientScan? PatientScan { get; set; }
}
