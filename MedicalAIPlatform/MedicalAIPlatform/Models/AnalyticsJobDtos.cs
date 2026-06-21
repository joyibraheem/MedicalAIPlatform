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
