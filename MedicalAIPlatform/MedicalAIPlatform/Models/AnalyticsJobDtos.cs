using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

/// <summary>Lightweight CT analysis outcome for polling APIs / chat UI (no large preview payloads).</summary>
public sealed class AnalyticsCtJobResultDto
{
    [JsonPropertyName("redirect")]
    public string Redirect { get; init; } = "";

    [JsonPropertyName("predictedClass")]
    public string PredictedClass { get; init; } = "";

    [JsonPropertyName("scanType")]
    public string ScanType { get; init; } = "";

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    [JsonPropertyName("topProbabilities")]
    public Dictionary<string, double> TopProbabilities { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("inferenceNote")]
    public string? InferenceNote { get; init; }

    [JsonPropertyName("modelUsed")]
    public string ModelUsed { get; init; } = "";

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; init; } = "";

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("inferenceMs")]
    public int? InferenceMs { get; init; }

    [JsonPropertyName("dataset")]
    public string Dataset { get; init; } = "";

    [JsonPropertyName("trainingDate")]
    public string TrainingDate { get; init; } = "";

    [JsonPropertyName("predictionTimeUtc")]
    public DateTimeOffset? PredictionTimeUtc { get; init; }

    [JsonPropertyName("xRayModelId")]
    public string XRayModelId { get; init; } = ChestXRayModels.CheXNet;
}

/// <summary>GET /api/job/status/:id response.</summary>
public sealed class AnalyticsJobStatusDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("result")]
    public AnalyticsCtJobResultDto? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    /// <summary>Set when kind is assistant_chat.</summary>
    [JsonPropertyName("assistantContent")]
    public string? AssistantContent { get; init; }
}
