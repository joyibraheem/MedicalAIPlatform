using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

public sealed class CheXNetPredictionResponse
{
    [JsonPropertyName("class_names")]
    public string[] ClassNames { get; set; } = [];

    [JsonPropertyName("probabilities")]
    public Dictionary<string, double> Probabilities { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("topk")]
    public List<CheXNetTopKItem> TopK { get; set; } = [];

    [JsonPropertyName("heatmap")]
    public CheXNetHeatmap Heatmap { get; set; } = new();

    [JsonPropertyName("pneumonia_probability")]
    public double PneumoniaProbability { get; set; }

    [JsonPropertyName("device")]
    public string Device { get; set; } = "";

    [JsonPropertyName("inference_ms")]
    public int InferenceMs { get; set; }

    [JsonPropertyName("model_used")]
    public string? ModelUsed { get; set; }

    [JsonPropertyName("model_version")]
    public string? ModelVersion { get; set; }

    [JsonPropertyName("dataset")]
    public string? Dataset { get; set; }

    [JsonPropertyName("training_date")]
    public string? TrainingDate { get; set; }

    [JsonPropertyName("predicted_class")]
    public string? PredictedClass { get; set; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }

    [JsonPropertyName("scan_type")]
    public string? ScanType { get; set; }

    /// <summary>Server-built JPEG/PNG thumbnail for DICOM-derived results (ignored for JSON).</summary>
    [JsonIgnore]
    public string PreviewImageMime { get; set; } = "image/jpeg";

    [JsonIgnore]
    public string PreviewImageBase64 { get; set; } = "";

    [JsonIgnore]
    public string PreviewImageDataUrl =>
        string.IsNullOrWhiteSpace(PreviewImageBase64)
            ? ""
            : $"data:{(string.IsNullOrWhiteSpace(PreviewImageMime) ? "image/jpeg" : PreviewImageMime)};base64,{PreviewImageBase64}";
}

public sealed class CheXNetTopKItem
{
    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("probability")]
    public double Probability { get; set; }
}

public sealed class CheXNetHeatmap
{
    [JsonPropertyName("class_name")]
    public string ClassName { get; set; } = "";

    [JsonPropertyName("mime")]
    public string Mime { get; set; } = "image/png";

    [JsonPropertyName("image_base64")]
    public string ImageBase64 { get; set; } = "";

    [JsonIgnore]
    public string DataUrl => string.IsNullOrWhiteSpace(ImageBase64)
        ? ""
        : $"data:{Mime};base64,{ImageBase64}";
}

public sealed class BioBertEntity
{
    [JsonPropertyName("entity_group")]
    public string EntityGroup { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("word")]
    public string Word { get; set; } = "";

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }
}

public sealed class BioBertResponse
{
    [JsonPropertyName("entities")]
    public List<BioBertEntity> Entities { get; set; } = [];
}

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

    /// <summary>Rendered JPEG from DICOM pipeline for browser display (ignored for JSON).</summary>
    [JsonIgnore]
    public string PreviewImageMime { get; set; } = "image/jpeg";

    [JsonIgnore]
    public string PreviewImageBase64 { get; set; } = "";

    [JsonIgnore]
    public string PreviewImageDataUrl =>
        string.IsNullOrWhiteSpace(PreviewImageBase64)
            ? ""
            : $"data:{(string.IsNullOrWhiteSpace(PreviewImageMime) ? "image/jpeg" : PreviewImageMime)};base64,{PreviewImageBase64}";
}
