namespace MedicalAIPlatform.Models;

/// <summary>Doctor validation/correction of an ML prediction for human-in-the-loop retraining workflows.</summary>
public sealed class PredictionFeedback
{
    public Guid Id { get; set; }

    /// <summary>AspNetUsers.Id of submitting clinician.</summary>
    public string SubmittingDoctorUserId { get; set; } = "";

    /// <summary>Optional link to async ChestAI job (CT pipeline).</summary>
    public Guid? RelatedJobId { get; set; }

    /// <summary>Browser/session scoped id generated per result panel (dedupe + traceability).</summary>
    public Guid ClientSessionCorrelationId { get; set; }

    /// <summary>Imaging modality or route: CT, XR, TEXT, COMBINED.</summary>
    public string Modality { get; set; } = "";

    /// <summary>Model identifier e.g. LungAI, CheXNet, BioBERT.</summary>
    public string ModelKey { get; set; } = "";

    public string? StudyInstanceUid { get; set; }

    public string? SeriesInstanceUid { get; set; }

    /// <summary>Snapshot of model output (JSON). Exclude large pixel payloads at capture time.</summary>
    public string OriginalPredictionJson { get; set; } = "";

    /// <summary>Accept | Modify</summary>
    public string DoctorAction { get; set; } = "";

    public string? CorrectedPrimaryLabel { get; set; }

    /// <summary>Doctor-assigned confidence for primary label (0–1).</summary>
    public double? CorrectedPrimaryConfidence { get; set; }

    public string? CorrectedProbabilitiesJson { get; set; }

    public string? ClinicalNotes { get; set; }

    /// <summary>Optional pointers for offline asset resolution (paths, blob keys). Never PHI in logs.</summary>
    public string? TrainingAssetPointerJson { get; set; }

    /// <summary>Pending | Approved | Rejected</summary>
    public string ReviewStatus { get; set; } = PredictionFeedbackStatuses.ReviewPending;

    public string? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExportedForTrainingAt { get; set; }

    public string? TrainingExportBatchId { get; set; }
}
