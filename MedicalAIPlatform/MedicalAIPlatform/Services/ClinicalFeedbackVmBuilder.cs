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

    public static ClinicalFeedbackPanelVm FromLung(
        LungAICtResponse r,
        AnalyticsStateService? state = null,
        string modality = "CT",
        Guid? relatedJobId = null)
    {
        var snap = new
        {
            r.ScanType,
            r.PredictedClass,
            r.Probabilities,
            r.Error
        };
        var keys = (r.Probabilities ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)).Keys.ToList();
        var source = state?.LungCancerSourceJson;
        var sourceObj = TrainingSourceData.FromJson(source);
        return BuildPanel("LungAI", modality, snap, keys, source, sourceObj, relatedJobId ?? state?.RelatedJobId);
    }

    public static ClinicalFeedbackPanelVm FromCheXNet(
        CheXNetPredictionResponse r,
        AnalyticsStateService? state = null,
        string modality = "XR",
        Guid? relatedJobId = null) =>
        FromChestXRay(r, ChestXRayModels.CheXNet, state, modality, relatedJobId);

    public static ClinicalFeedbackPanelVm FromChestXRay(
        CheXNetPredictionResponse r,
        string modelKey,
        AnalyticsStateService? state = null,
        string modality = "XR",
        Guid? relatedJobId = null)
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
            r.ModelUsed,
            r.ModelVersion,
            r.Dataset,
            r.TrainingDate,
            heatmapClassName = r.Heatmap?.ClassName
        };
        var keys = probs.Keys.ToList();
        var sourceObj = TrainingSourceData.FromJson(state?.CheXNetSourceJson);
        var normalizedKey = ChestXRayModels.Normalize(modelKey);
        return BuildPanel(normalizedKey, modality, snap, keys, state?.CheXNetSourceJson, sourceObj, relatedJobId ?? state?.RelatedJobId);
    }

    public static ClinicalFeedbackPanelVm FromBio(
        BioBertResponse r,
        AnalyticsStateService? state = null,
        string modality = "TEXT",
        Guid? relatedJobId = null)
    {
        var ents = (r.Entities ?? []).Take(80).Select(e => new { e.Word, e.EntityGroup, e.Score }).ToList();
        var snap = new { entities = ents };
        var labelChoices = ents.Select(e => e.Word).Where(w => !string.IsNullOrWhiteSpace(w)).Distinct().Take(48).ToList();
        var sourceObj = TrainingSourceData.FromJson(state?.BioBertSourceJson);
        return BuildPanel("BioBERT", modality, snap, labelChoices, state?.BioBertSourceJson, sourceObj, relatedJobId ?? state?.RelatedJobId);
    }

    private static ClinicalFeedbackPanelVm BuildPanel(
        string modelKey,
        string modality,
        object snap,
        List<string> classNames,
        string? sourceJson,
        TrainingSourceData? sourceObj,
        Guid? relatedJobId)
    {
        return new ClinicalFeedbackPanelVm
        {
            CorrelationId = Guid.NewGuid(),
            Modality = modality,
            ModelKey = modelKey,
            PredictionJson = JsonSerializer.Serialize(snap, Opts),
            ClassNamesJson = JsonSerializer.Serialize(classNames, Opts),
            RelatedJobId = relatedJobId ?? sourceObj?.RelatedJobId,
            StudyInstanceUid = sourceObj?.StudyInstanceUid,
            SeriesInstanceUid = sourceObj?.SeriesInstanceUid,
            TrainingSourceJson = sourceJson
        };
    }
}
