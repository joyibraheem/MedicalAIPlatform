using System.Text.Json.Serialization;

namespace CheXNet.BlazorApp.Models;

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

