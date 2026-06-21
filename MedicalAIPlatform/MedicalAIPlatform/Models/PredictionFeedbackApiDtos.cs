using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

public sealed class SubmitPredictionFeedbackDto
{
    public Guid ClientSessionCorrelationId { get; set; }

    public Guid? RelatedJobId { get; set; }

    public string? StudyInstanceUid { get; set; }

    public string? SeriesInstanceUid { get; set; }

    public string Modality { get; set; } = "";

    public string ModelKey { get; set; } = "";

    /// <summary>Original prediction JSON (snapshot).</summary>
    public string OriginalPredictionJson { get; set; } = "";

    public string DoctorAction { get; set; } = "";

    public string? CorrectedPrimaryLabel { get; set; }

    public double? CorrectedPrimaryConfidence { get; set; }

    public Dictionary<string, double>? CorrectedProbabilities { get; set; }

    public string? ClinicalNotes { get; set; }

    public string? TrainingAssetPointerJson { get; set; }
}

public sealed class PredictionFeedbackListItemDto
{
    public Guid Id { get; init; }

    public string SubmittingDoctorUserId { get; init; } = "";

    public string? SubmittingDoctorEmail { get; init; }

    public string Modality { get; init; } = "";

    public string ModelKey { get; init; } = "";

    public string DoctorAction { get; init; } = "";

    public string ReviewStatus { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public string OriginalPredictionJson { get; init; } = "";

    public string? CorrectedPrimaryLabel { get; init; }

    public double? CorrectedPrimaryConfidence { get; init; }

    public string? CorrectedProbabilitiesJson { get; init; }

    public string? ClinicalNotes { get; init; }

    public Guid? RelatedJobId { get; init; }

    public Guid ClientSessionCorrelationId { get; init; }
}

public sealed class ReviewPredictionFeedbackDto
{
    public string? ReviewNotes { get; set; }
}

public sealed class MarkExportedDto
{
    public List<Guid> FeedbackIds { get; set; } = [];

    public string BatchId { get; set; } = "";
}

/// <summary>Normalized row for offline training pipelines (approved labels only).</summary>
public sealed class TrainingExportRowDto
{
    public Guid FeedbackId { get; init; }

    public string Modality { get; init; } = "";

    public string ModelKey { get; init; } = "";

    [JsonPropertyName("gold_label")]
    public string GoldLabel { get; init; } = "";

    [JsonPropertyName("gold_confidence")]
    public double? GoldConfidence { get; init; }

    [JsonPropertyName("gold_probabilities")]
    public Dictionary<string, double>? GoldProbabilities { get; init; }

    [JsonPropertyName("original_prediction")]
    public string OriginalPredictionJson { get; init; } = "";

    public Guid? RelatedJobId { get; init; }

    public string? StudyInstanceUid { get; init; }

    public string? ClinicalNotes { get; init; }

    public DateTimeOffset ApprovedAt { get; init; }

    public string? TrainingAssetPointerJson { get; init; }
}
