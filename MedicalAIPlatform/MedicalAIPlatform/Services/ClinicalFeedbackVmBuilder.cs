using System.Text.Json;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

/// <summary>Builds compact JSON snapshots for HITL panels (no raw image bytes).</summary>
public static class ClinicalFeedbackVmBuilder
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static ClinicalFeedbackPanelVm FromLung(LungAICtResponse r, string modality = "CT", Guid? relatedJobId = null)
    {
        var snap = new
        {
            r.ScanType,
            r.PredictedClass,
            r.Probabilities,
            r.Error
        };
        var keys = (r.Probabilities ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)).Keys.ToList();
        return new ClinicalFeedbackPanelVm
        {
            CorrelationId = Guid.NewGuid(),
            Modality = modality,
            ModelKey = "LungAI",
            PredictionJson = JsonSerializer.Serialize(snap, Opts),
            ClassNamesJson = JsonSerializer.Serialize(keys, Opts),
            RelatedJobId = relatedJobId
        };
    }

    public static ClinicalFeedbackPanelVm FromCheXNet(CheXNetPredictionResponse r, string modality = "XR")
    {
        var probs = r.Probabilities ?? new Dictionary<string, double>(StringComparer.Ordinal);
        KeyValuePair<string, double>? topKv = probs.Count > 0
            ? probs.OrderByDescending(kv => kv.Value).First()
            : null;
        var snap = new
        {
            r.PneumoniaProbability,
            r.Probabilities,
            topClass = topKv?.Key,
            topProb = topKv?.Value,
            r.InferenceMs,
            heatmapClassName = r.Heatmap?.ClassName
        };
        var keys = probs.Keys.ToList();
        return new ClinicalFeedbackPanelVm
        {
            CorrelationId = Guid.NewGuid(),
            Modality = modality,
            ModelKey = "CheXNet",
            PredictionJson = JsonSerializer.Serialize(snap, Opts),
            ClassNamesJson = JsonSerializer.Serialize(keys, Opts)
        };
    }

    public static ClinicalFeedbackPanelVm FromBio(BioBertResponse r, string modality = "TEXT")
    {
        var ents = (r.Entities ?? []).Take(80).Select(e => new { e.Word, e.EntityGroup, e.Score }).ToList();
        var snap = new { entities = ents };
        var labelChoices = ents.Select(e => e.Word).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().Take(48).ToList();
        return new ClinicalFeedbackPanelVm
        {
            CorrelationId = Guid.NewGuid(),
            Modality = modality,
            ModelKey = "BioBERT",
            PredictionJson = JsonSerializer.Serialize(snap, Opts),
            ClassNamesJson = JsonSerializer.Serialize(labelChoices, Opts)
        };
    }
}
