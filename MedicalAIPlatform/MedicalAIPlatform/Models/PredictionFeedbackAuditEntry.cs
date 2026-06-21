namespace MedicalAIPlatform.Models;

/// <summary>Immutable audit trail for feedback lifecycle (submission, review, export).</summary>
public sealed class PredictionFeedbackAuditEntry
{
    public long Id { get; set; }

    public Guid PredictionFeedbackId { get; set; }

    public string ActorUserId { get; set; } = "";

    /// <summary>Submitted | Approved | Rejected | ExportMarked | Updated</summary>
    public string Action { get; set; } = "";

    public string? DetailJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
