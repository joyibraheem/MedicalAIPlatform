namespace MedicalAIPlatform.Models;

/// <summary>Razor partial payload for HITL Accept / Modify UI.</summary>
public sealed class ClinicalFeedbackPanelVm
{
    public Guid CorrelationId { get; init; }

    public string Modality { get; init; } = "";

    public string ModelKey { get; init; } = "";

    /// <summary>JSON object string (no raw images).</summary>
    public string PredictionJson { get; init; } = "{}";

    /// <summary>JSON array of class / label strings for modify dropdown.</summary>
    public string ClassNamesJson { get; init; } = "[]";

    public Guid? RelatedJobId { get; init; }

    public string? StudyInstanceUid { get; init; }

    public string? SeriesInstanceUid { get; init; }
}
