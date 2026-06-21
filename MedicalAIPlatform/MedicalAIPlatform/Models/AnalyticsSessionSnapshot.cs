using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

/// <summary>Per-user analytics workspace state (CheXNet, BioBERT, LungAI, previews).</summary>
public sealed class AnalyticsSessionSnapshot
{
    public static AnalyticsSessionSnapshot Empty { get; } = new();

    [JsonPropertyName("currentResults")]
    public Dictionary<string, CheXNetPredictionResponse>? CurrentResults { get; init; }

    [JsonPropertyName("currentBioBertResults")]
    public BioBertResponse? CurrentBioBertResults { get; init; }

    [JsonPropertyName("currentLungAIResults")]
    public LungAICtResponse? CurrentLungAIResults { get; init; }

    [JsonPropertyName("pipelineNotes")]
    public IReadOnlyList<string> PipelineNotes { get; init; } = [];

    [JsonPropertyName("xRayImageDataUrl")]
    public string? XRayImageDataUrl { get; init; }

    [JsonPropertyName("ctImageDataUrl")]
    public string? CTImageDataUrl { get; init; }

    [JsonPropertyName("clinicalText")]
    public string? ClinicalText { get; init; }

    /// <summary>Merges <paramref name="patch"/> into <paramref name="existing"/>; non-null patch fields win.</summary>
    public static AnalyticsSessionSnapshot Merge(AnalyticsSessionSnapshot existing, AnalyticsSessionSnapshot patch)
    {
        return new AnalyticsSessionSnapshot
        {
            CurrentResults = patch.CurrentResults ?? existing.CurrentResults,
            CurrentBioBertResults = patch.CurrentBioBertResults ?? existing.CurrentBioBertResults,
            CurrentLungAIResults = patch.CurrentLungAIResults ?? existing.CurrentLungAIResults,
            XRayImageDataUrl = patch.XRayImageDataUrl ?? existing.XRayImageDataUrl,
            CTImageDataUrl = patch.CTImageDataUrl ?? existing.CTImageDataUrl,
            ClinicalText = patch.ClinicalText ?? existing.ClinicalText,
            PipelineNotes = patch._pipelineNotesProvided
                ? patch.PipelineNotes
                : existing.PipelineNotes
        };
    }

    [JsonIgnore]
    internal bool _pipelineNotesProvided { get; init; }

    public static AnalyticsSessionSnapshot FromSetResults(
        Dictionary<string, CheXNetPredictionResponse>? chexnetResults,
        BioBertResponse? bioBertResults,
        LungAICtResponse? lungAIResults,
        string? xrayImageDataUrl,
        string? ctImageDataUrl,
        string? clinicalText,
        IReadOnlyList<string>? pipelineNotes,
        bool pipelineNotesProvided)
    {
        return new AnalyticsSessionSnapshot
        {
            CurrentResults = chexnetResults,
            CurrentBioBertResults = bioBertResults,
            CurrentLungAIResults = lungAIResults,
            XRayImageDataUrl = xrayImageDataUrl,
            CTImageDataUrl = ctImageDataUrl,
            ClinicalText = clinicalText,
            PipelineNotes = pipelineNotes ?? [],
            _pipelineNotesProvided = pipelineNotesProvided
        };
    }
}
