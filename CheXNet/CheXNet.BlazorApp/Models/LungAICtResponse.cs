using System.Text.Json.Serialization;

namespace CheXNet.BlazorApp.Models;

/// <summary>
/// Response from LungAI CT scan model (ResNet lung cancer classification).
/// </summary>
public sealed class LungAICtResponse
{
    [JsonPropertyName("scan_type")]
    public string ScanType { get; set; } = "CT";

    [JsonPropertyName("predicted_class")]
    public string PredictedClass { get; set; } = "";

    [JsonPropertyName("class_names")]
    public string[] ClassNames { get; set; } = [];

    [JsonPropertyName("probabilities")]
    public Dictionary<string, double> Probabilities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
