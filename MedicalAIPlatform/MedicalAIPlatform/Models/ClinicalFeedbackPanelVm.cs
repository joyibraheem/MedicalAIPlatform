namespace MedicalAIPlatform.Models;

/// <summary>Razor partial payload for HITL Accept / Modify UI.</summary>
public sealed class ClinicalFeedbackPanelVm
{
    public Guid CorrelationId { get; init; }

    public string Modality { get; init; } = "";

    public string ModelKey { get; init; } = "";

    public string PredictionJson { get; init; } = "{}";

    public string ClassNamesJson { get; init; } = "[]";

    public Guid? RelatedJobId { get; init; }

    public string? StudyInstanceUid { get; init; }

    public string? SeriesInstanceUid { get; init; }

    /// <summary>Serialized <see cref="TrainingSourceData"/> for retraining asset linkage.</summary>
    public string? TrainingSourceJson { get; init; }
}
