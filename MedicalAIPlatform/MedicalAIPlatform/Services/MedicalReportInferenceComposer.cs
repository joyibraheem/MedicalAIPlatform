using System.Globalization;
using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

/// <summary>
/// Merges persisted <see cref="PatientScan"/> AI JSON with live session analytics outputs,
/// then builds structured AI analysis + imaging-forward narrative blocks for clinical reports.
/// </summary>
public static class MedicalReportInferenceComposer
{
    private static readonly JsonSerializerOptions DeserializeOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed record ComposeResult(
        MedicalReportAiAnalysisSnapshotDto AiAnalysis,
        string ImagingFindingsBlock,
        string Impression,
        string Recommendations,
        double Confidence,
        string ConfidenceMethod,
        List<MedicalReportImageRefDto> StudyImages);

    public static ComposeResult Compose(
        List<PatientScan> scansOrderedDesc,
        AnalyticsSessionSnapshot analytics)
    {
        var cxrMerged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var cxrSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ctMerged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var ctSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entityMerged = new Dictionary<string, (double Score, string Group, string Source)>(StringComparer.OrdinalIgnoreCase);

        var studyImages = new List<MedicalReportImageRefDto>();
        bool usedStudies = false;
        bool usedLive = false;

        foreach (var scan in scansOrderedDesc)
        {
            var dateLabel = scan.ScanDate.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var studyTag = $"Stored study ({scan.ScanType}, {dateLabel})";
            var analysis = scan.AiAnalysis;

            if (!string.IsNullOrWhiteSpace(analysis?.CheXNetResults))
            {
                var chex = TryDeserializeCheXNet(analysis.CheXNetResults);
                if (chex is not null)
                {
                    usedStudies = true;
                    UpsertProbabilities(cxrMerged, cxrSource, FlattenCheXNet(chex), studyTag);
                    AppendHeatmapIfAny(studyImages, chex.Heatmap, $"CheXNet heatmap ({studyTag})");
                    AppendPreviewIfAny(studyImages, chex.PreviewImageDataUrl, $"Imaging preview ({studyTag})");
                }
            }

            if (!string.IsNullOrWhiteSpace(analysis?.LungAIResults))
            {
                var lung = TryDeserializeLung(analysis.LungAIResults);
                if (lung is not null && string.IsNullOrWhiteSpace(lung.Error) && lung.Probabilities.Count > 0)
                {
                    usedStudies = true;
                    UpsertProbabilities(ctMerged, ctSource, lung.Probabilities, studyTag);
                    AppendPreviewIfAny(studyImages, lung.PreviewImageDataUrl, $"LungAI CT preview ({studyTag})");
                }
            }

            if (!string.IsNullOrWhiteSpace(analysis?.BioBertResults))
            {
                var bio = TryDeserializeBio(analysis.BioBertResults);
                if (bio?.Entities.Count > 0)
                {
                    usedStudies = true;
                    foreach (var e in bio.Entities)
                        UpsertEntity(entityMerged, e.Word, e.Score, e.EntityGroup ?? "", studyTag);
                }
            }
        }

        // Live session — same merge semantics (max probability wins).
        if (analytics.CurrentResults is { Count: > 0 })
        {
            usedLive = true;
            foreach (var kv in analytics.CurrentResults)
            {
                UpsertProbabilities(cxrMerged, cxrSource, FlattenCheXNet(kv.Value),
                    $"Live session ({kv.Key})");
                AppendHeatmapIfAny(studyImages, kv.Value.Heatmap,
                    $"CheXNet heatmap (live: {kv.Key}, {kv.Value.Heatmap.ClassName})");
                AppendPreviewIfAny(studyImages, kv.Value.PreviewImageDataUrl,
                    $"Imaging preview (live: {kv.Key})");
            }
        }

        if (analytics.CurrentLungAIResults is { } liveLung &&
            string.IsNullOrWhiteSpace(liveLung.Error) &&
            liveLung.Probabilities.Count > 0)
        {
            usedLive = true;
            UpsertProbabilities(ctMerged, ctSource, liveLung.Probabilities, "Live session (LungAI)");
            AppendPreviewIfAny(studyImages, liveLung.PreviewImageDataUrl, "LungAI CT preview (live)");
        }

        if (analytics.CurrentBioBertResults?.Entities.Count > 0)
        {
            usedLive = true;
            foreach (var e in analytics.CurrentBioBertResults.Entities)
                UpsertEntity(entityMerged, e.Word, e.Score, e.EntityGroup ?? "", "Live session (BioBERT)");
        }

        var cxrList = ToSnapshotRows(cxrMerged, cxrSource);
        var ctList = ToSnapshotRows(ctMerged, ctSource);

        var cxTopProb = 0d;
        var cxTopKey = "";
        if (cxrMerged.Count > 0)
        {
            var cxBest = cxrMerged.OrderByDescending(kv => kv.Value).First();
            cxTopKey = cxBest.Key;
            cxTopProb = cxBest.Value;
        }

        var ctTopProb = 0d;
        var ctTopKey = "";
        if (ctMerged.Count > 0)
        {
            var ctBest = ctMerged.OrderByDescending(kv => kv.Value).First();
            ctTopKey = ctBest.Key;
            ctTopProb = ctBest.Value;
        }

        string topLabel;
        double topProb;
        if (cxrMerged.Count > 0 && cxTopProb >= ctTopProb)
        {
            topLabel = cxTopKey;
            topProb = cxTopProb;
        }
        else if (ctMerged.Count > 0)
        {
            topLabel = ctTopKey;
            topProb = ctTopProb;
        }
        else
        {
            topLabel = "";
            topProb = 0;
        }

        var entitySnapshots = entityMerged
            .Select(kv => new MedicalReportEntitySnapshotDto
            {
                Text = kv.Key,
                Group = kv.Value.Group,
                Score = kv.Value.Score
            })
            .OrderByDescending(e => e.Score)
            .Take(16)
            .ToList();

        var riskTier = MapRiskTier(Math.Max(
            cxrMerged.Values.DefaultIfEmpty(0).Max(),
            ctMerged.Values.DefaultIfEmpty(0).Max()));

        var (confidence, confidenceMethod) = ComputeModelConfidence(cxrMerged, ctMerged, entitySnapshots);

        var heatmapNote = "";
        if (studyImages.Any(i => i.Label.Contains("heatmap", StringComparison.OrdinalIgnoreCase)))
            heatmapNote =
                "Salience overlays (heatmaps) are attached above when available; they highlight regions that drove the chest model score and must not be read as standalone diagnoses.";
        else if (analytics.CurrentResults is { Count: > 0 })
            heatmapNote =
                "Heatmaps may be generated from the live analytics session when enabled — refer to the imaging analytics workspace for interactive overlays.";

        var caption =
            cxrMerged.Count > 0
                ? "Confidence reflects the highest merged CheXNet pathology probability across stored studies and/or the current analytics session."
                : ctMerged.Count > 0
                    ? "Confidence reflects the dominant LungAI CT class probability after merging stored volumetric runs and live session output."
                    : entitySnapshots.Count > 0
                        ? "No chest or CT class probabilities were merged; auxiliary confidence follows top structured text entities (BioBERT)."
                        : "No quantitative model outputs were available; narrative sections emphasize chart context only.";

        var aiAnalysis = new MedicalReportAiAnalysisSnapshotDto
        {
            ChexnetProbabilities = cxrList,
            CtProbabilities = ctList,
            ClinicalEntities = entitySnapshots,
            TopConditionLabel = topLabel,
            TopConditionProbability = topProb,
            RiskTier = riskTier,
            ConfidenceCaption = caption,
            HeatmapNote = heatmapNote,
            UsedPatientStudies = usedStudies,
            UsedLiveSession = usedLive
        };

        var imagingFindings = BuildImagingFindings(cxrMerged, ctMerged, entitySnapshots);
        var impression = BuildImpression(topLabel, topProb, cxrMerged, ctMerged, riskTier);
        var recommendations = BuildSmartRecommendations(cxrMerged, ctMerged);

        return new ComposeResult(aiAnalysis, imagingFindings, impression, recommendations, confidence,
            confidenceMethod, studyImages);
    }

    private static void UpsertProbabilities(
        Dictionary<string, double> probs,
        Dictionary<string, string> sources,
        IReadOnlyDictionary<string, double> incoming,
        string sourceTag)
    {
        foreach (var kv in incoming)
        {
            var label = kv.Key.Trim();
            if (label.Length == 0 || kv.Value is <= 0 or > 1)
                continue;

            if (!probs.TryGetValue(label, out var cur) || kv.Value > cur)
            {
                probs[label] = kv.Value;
                sources[label] = sourceTag;
            }
        }
    }

    private static void UpsertEntity(
        Dictionary<string, (double Score, string Group, string Source)> map,
        string text,
        double score,
        string group,
        string source)
    {
        var key = text.Trim();
        if (key.Length == 0 || score <= 0)
            return;

        if (!map.TryGetValue(key, out var cur) || score > cur.Score)
            map[key] = (score, group, source);
    }

    private static Dictionary<string, double> FlattenCheXNet(CheXNetPredictionResponse r)
    {
        var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in r.Probabilities)
            d[kv.Key] = kv.Value;
        foreach (var row in r.TopK)
        {
            if (!string.IsNullOrWhiteSpace(row.ClassName))
                d[row.ClassName] = row.Probability;
        }

        return d;
    }

    private static List<MedicalReportProbabilitySnapshotDto> ToSnapshotRows(
        Dictionary<string, double> probs,
        Dictionary<string, string> sources)
    {
        return probs
            .OrderByDescending(kv => kv.Value)
            .Select(kv => new MedicalReportProbabilitySnapshotDto
            {
                Label = kv.Key,
                Probability = kv.Value,
                Source = sources.TryGetValue(kv.Key, out var s) ? s : ""
            })
            .Take(20)
            .ToList();
    }

    private static (double Confidence, string Method) ComputeModelConfidence(
        Dictionary<string, double> cxr,
        Dictionary<string, double> ct,
        List<MedicalReportEntitySnapshotDto> entities)
    {
        if (cxr.Count > 0)
        {
            var m = cxr.Values.Max();
            return (Math.Round(Math.Clamp(m, 0, 1), 4),
                "maximum_merged_chexnet_probability");
        }

        if (ct.Count > 0)
        {
            var m = ct.Values.Max();
            return (Math.Round(Math.Clamp(m, 0, 1), 4),
                "maximum_merged_lungai_probability");
        }

        if (entities.Count > 0)
        {
            var m = entities.Max(e => e.Score);
            return (Math.Round(Math.Clamp(m, 0, 1), 4),
                "top_biobert_entity_score");
        }

        return (0.48,
            "no_model_scores_chart_context_only");
    }

    private static string MapRiskTier(double maxProb)
    {
        if (maxProb >= 0.70) return "high";
        if (maxProb >= 0.45) return "medium";
        if (maxProb >= 0.22) return "low";
        return "minimal";
    }

    private static string BuildImagingFindings(
        Dictionary<string, double> cxr,
        Dictionary<string, double> ct,
        List<MedicalReportEntitySnapshotDto> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Imaging interpretation (AI-assisted, data-driven) ===");
        sb.AppendLine();

        if (cxr.Count == 0 && ct.Count == 0)
        {
            sb.AppendLine(
                "• No CheXNet or LungAI class probabilities were merged from patient studies or the active analytics session.");
            sb.AppendLine(
                "  Upload or run analytics on chest imaging to populate quantitative findings.");
        }
        else
        {
            sb.AppendLine("Structured probability-driven observations:");
            foreach (var kv in cxr.OrderByDescending(x => x.Value).Take(10))
                sb.AppendLine(ImagingLineForLabel(kv.Key, kv.Value, "chest"));

            foreach (var kv in ct.OrderByDescending(x => x.Value).Take(8))
                sb.AppendLine(ImagingLineForLabel(kv.Key, kv.Value, "CT"));

            sb.AppendLine();
            sb.AppendLine(SynthesizeDominantPattern(cxr, ct));
        }

        if (entities.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Clinical text modeling highlights (BioBERT, merged scores):");
            foreach (var e in entities.Take(10))
                sb.AppendLine($"• {e.Text} ({e.Group}) — {e.Score:P0}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ImagingLineForLabel(string label, double p, string modalityHint)
    {
        var tier = p switch
        {
            >= 0.72 => "High modeled likelihood",
            >= 0.48 => "Moderate modeled signal",
            >= 0.28 => "Low-to-moderate modeled probability",
            _ => "Low modeled probability"
        };

        var anatomy = modalityHint == "CT" ? "on volumetric CT modeling" : "on frontal chest modeling";

        return $"• {tier} for **{label}** ({p:P0}) {anatomy}.";
    }

    private static string SynthesizeDominantPattern(Dictionary<string, double> cxr, Dictionary<string, double> ct)
    {
        double PickProb(params string[] needles) =>
            cxr.Where(kv => needles.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => kv.Value).DefaultIfEmpty(0).Max();

        var pneu = Math.Max(PickProb("Pneumonia", "Consolidation", "Infiltration"),
            CtProbContaining(ct, "pneumonia"));
        var eff = PickProb("Effusion");
        var mass = Math.Max(Math.Max(PickProb("Mass", "Nodule"), CtProbContaining(ct, "mass")),
            CtProbContaining(ct, "nodule"));
        var edema = PickProb("Edema");
        var cardio = PickProb("Cardiomegaly");

        var lines = new List<string>();
        if (pneu >= 0.35)
            lines.Add(
                "Airspace / infectious-pattern signal is elevated enough to warrant acute clinical correlation (consider alternate diagnoses such as aspiration or atypical infection per presentation).");
        if (eff >= 0.35)
            lines.Add(
                "Pleural-space modeling suggests fluid signal — differentiate transudative processes (e.g., heart failure) versus exudative causes when clinically appropriate.");
        if (edema >= 0.35 || cardio >= 0.45)
            lines.Add(
                "Cardiopulmonary congestion metrics trend upward on modeling — heart failure-related pulmonary edema remains in the differential.");
        if (mass >= 0.40)
            lines.Add(
                "A focal lesion signal is present at modeled thresholds; institutional nodule/mass pathways apply.");
        if (lines.Count == 0 && cxr.Count + ct.Count > 0)
            lines.Add(
                "No single pathology class exceeds intermediate heuristic thresholds; interpretation favors multidisciplinary correlation rather than a definitive automated pattern.");

        return string.Join("\n", lines.Select(l => $"• {l}"));
    }

    private static string BuildImpression(
        string topLabel,
        double topProb,
        Dictionary<string, double> cxr,
        Dictionary<string, double> ct,
        string riskTier)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "**AI-assisted impression for workflow triage — not a finalized radiology report or diagnosis.**");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(topLabel) && topProb > 0)
            sb.AppendLine(
                $"The dominant modeled abnormality is **{topLabel}** with merged probability **{topProb:P0}** (risk tier: **{riskTier}**).");
        else
            sb.AppendLine(
                "Quantitative imaging class probabilities were unavailable after merging stored studies and session analytics; prioritize technician verification that models ran successfully.");

        sb.AppendLine();

        var differentials = new List<string>();
        if (LabelFamilyHigh(cxr, "Pneumonia", "Consolidation", "Infiltration"))
            differentials.Add("community-acquired pneumonia vs. pulmonary edema vs. aspiration");
        if (LabelFamilyHigh(cxr, "Effusion"))
            differentials.Add("congestive heart failure vs. parapneumonic effusion vs. other exudative causes");
        if (LabelFamilyHigh(cxr, "Edema", "Cardiomegaly"))
            differentials.Add("cardiogenic pulmonary edema vs. fluid overload");
        if (ct.Values.Any(v => v >= 0.45))
            differentials.Add("primary pulmonary malignancy surveillance pathways vs. benign granulomatous disease");

        if (differentials.Count > 0)
            sb.AppendLine(
                $"Differential anchors informed by model emphasis include: {string.Join("; ", differentials)}.");
        else if (cxr.Count + ct.Count > 0)
            sb.AppendLine(
                "Model outputs are heterogeneous without a single dominant class — maintain broad differential until formal imaging review.");

        sb.AppendLine();
        sb.AppendLine(
            "Correlation with prior imaging, laboratory data, physical examination, and attending interpretation remains mandatory prior to therapeutic decisions.");

        return sb.ToString().TrimEnd();
    }

    private static bool LabelFamilyHigh(Dictionary<string, double> cxr, params string[] needles) =>
        cxr.Any(kv =>
            needles.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase)) && kv.Value >= 0.40);

    private static string BuildSmartRecommendations(
        Dictionary<string, double> cxr,
        Dictionary<string, double> ct)
    {
        var lines = new List<string>();

        double P(params string[] needles) =>
            cxr.Where(kv => needles.Any(n => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase)))
                .Select(kv => kv.Value).DefaultIfEmpty(0).Max();

        if (P("Pneumonia", "Consolidation", "Infiltration") >= 0.70)
            lines.Add(
                "Chest imaging probability for pneumonia-pattern disease exceeds 70% — expedited clinician review and guideline-concordant infectious work-up if clinically indicated.");
        else if (P("Pneumonia", "Consolidation", "Infiltration") >= 0.45)
            lines.Add(
                "Intermediate pneumonia-modeled probability — recommend symptom-directed evaluation and repeat imaging only if clinical trajectory changes.");

        if (P("Effusion") >= 0.45)
            lines.Add(
                "Pleural effusion signal on modeling — consider lateral decubitus imaging or ultrasound correlation if symptoms persist.");

        if (Math.Max(P("Nodule", "Mass"), ct.Values.DefaultIfEmpty(0).Max()) >= 0.55)
            lines.Add(
                "Focal lesion probability exceeds surveillance heuristic — align follow-up with institutional pulmonary nodule guidelines.");

        if (P("Edema", "Cardiomegaly") >= 0.50)
            lines.Add(
                "Cardiopulmonary congestion indicators — evaluate volume status and cardiac risk factors per standard-of-care pathways.");

        lines.Add(
            "Maintain contemporaneous physician sign-off on all narrative sections prior to release outside the care team.");
        lines.Add(
            "Clearly label AI-assisted segments for medicolegal traceability and patient-facing education.");

        if (cxr.Count == 0 && ct.Count == 0)
            lines.Add(
                "Re-run analytics after importing imaging studies so quantitative thresholds can drive tailored recommendations.");

        return string.Join("\n", lines.Select(l => $"• {l}"));
    }

    private static CheXNetPredictionResponse? TryDeserializeCheXNet(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CheXNetPredictionResponse>(json, DeserializeOpts);
        }
        catch
        {
            return null;
        }
    }

    private static LungAICtResponse? TryDeserializeLung(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LungAICtResponse>(json, DeserializeOpts);
        }
        catch
        {
            return null;
        }
    }

    private static BioBertResponse? TryDeserializeBio(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BioBertResponse>(json, DeserializeOpts);
        }
        catch
        {
            return null;
        }
    }

    private static void AppendHeatmapIfAny(List<MedicalReportImageRefDto> list, CheXNetHeatmap hm, string label)
    {
        var url = hm.DataUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;
        if (list.Any(x => x.DataUrl == url))
            return;
        list.Add(new MedicalReportImageRefDto { Label = label, DataUrl = url });
    }

    private static void AppendPreviewIfAny(List<MedicalReportImageRefDto> list, string? dataUrl, string label)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            return;
        if (list.Any(x => x.DataUrl == dataUrl))
            return;
        list.Add(new MedicalReportImageRefDto { Label = label, DataUrl = dataUrl });
    }

    private static double CtProbContaining(Dictionary<string, double> ct, string needle)
    {
        foreach (var kv in ct)
        {
            if (kv.Key.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return 0;
    }
}
