using MedicalAIPlatform.Models.Dicom;

namespace MedicalAIPlatform.Models;

public sealed class CtScanViewerSession
{
    public Guid SessionId { get; init; }
    public string UserId { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public byte[] SourceBytes { get; init; } = [];
    public PatientMedicalHistory Metadata { get; init; } = new();
    public IReadOnlyList<CtSeriesInfo> Series { get; init; } = [];
    public string TempDirectory { get; init; } = "";
    public LungAICtResponse? Analysis { get; set; }
    /// <summary>Unified analysis panel for the viewer UI (CheXNet or LungAI).</summary>
    public CtScanAnalysisPanelDto? AnalysisPanel { get; set; }
    /// <summary>Last AI model id used for analysis (<see cref="DicomViewerAiModels"/>).</summary>
    public string? SelectedAiModel { get; set; }
    public int? PatientScanId { get; init; }
    public int? PatientId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Legacy flat list — first series slices only.</summary>
    public IReadOnlyList<CtScanSliceInfo> Slices => Series.Count > 0 ? Series[0].Slices : [];
}

public sealed class CtSeriesInfo
{
    public int SeriesIndex { get; init; }
    public string SeriesInstanceUid { get; init; } = "";
    public string Label { get; init; } = "";
    public string Modality { get; init; } = "";
    public string? SeriesDescription { get; init; }
    public string? BodyPartExamined { get; init; }
    public int PreviewSliceIndex { get; init; }
    public IReadOnlyList<CtScanSliceInfo> Slices { get; init; } = [];
}

public sealed class CtScanSliceInfo
{
    public int Index { get; init; }
    public string? InstanceNumber { get; init; }
    public string Label { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string JpegFileName { get; init; } = "";
}

public sealed class CtScanSessionSummaryDto
{
    public Guid SessionId { get; init; }
    public string FileName { get; init; } = "";
    public int SliceCount { get; init; }
    public int SeriesCount { get; init; }
    /// <summary>single | grid</summary>
    public string LayoutMode { get; init; } = "single";
    public PatientMedicalHistory Metadata { get; init; } = new();
    public IReadOnlyList<CtSeriesSummaryDto> Series { get; init; } = [];
    public bool HasAnalysis { get; init; }
    public int? PatientScanId { get; init; }
    public int? PatientId { get; init; }

    /// <summary>Legacy — active (first) series slices.</summary>
    public IReadOnlyList<CtScanSliceInfo> Slices =>
        Series.Count > 0 ? Series[0].Slices : [];
}

public sealed class CtSeriesSummaryDto
{
    public int SeriesIndex { get; init; }
    public string Label { get; init; } = "";
    public string Modality { get; init; } = "";
    public string? SeriesDescription { get; init; }
    public int SliceCount { get; init; }
    public int PreviewSliceIndex { get; init; }
    public IReadOnlyList<CtScanSliceInfo> Slices { get; init; } = [];
}

public sealed class CtScanAnalysisPanelDto
{
    public string PredictedDisease { get; init; } = "";
    public double ConfidenceScore { get; init; }
    public string RiskLevel { get; init; } = "";
    public string ModelName { get; init; } = "";
    public Dictionary<string, double> Probabilities { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Error { get; init; }
    public long? InferenceMs { get; init; }
}

public static class CtScanRiskMapper
{
    public static string MapRiskLevel(double maxProb)
    {
        if (maxProb >= 0.70) return "High";
        if (maxProb >= 0.45) return "Medium";
        if (maxProb >= 0.22) return "Low";
        return "Minimal";
    }

    public static string ModelDisplayName(LungAICtResponse response) =>
        string.Equals(response.ScanType, "CT-DICOM", StringComparison.OrdinalIgnoreCase)
            ? "LungAI · DICOM slice pipeline (MaxPooling)"
            : "LungAI · /predict/ct";

    public static CtScanAnalysisPanelDto ToPanel(LungAICtResponse response)
    {
        var probs = response.Probabilities ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var topProb = probs.Count == 0
            ? 0.0
            : probs.Values.Max();

        return new CtScanAnalysisPanelDto
        {
            PredictedDisease = string.IsNullOrWhiteSpace(response.PredictedClass) ? "—" : response.PredictedClass,
            ConfidenceScore = Math.Round(topProb, 4),
            RiskLevel = MapRiskLevel(topProb),
            ModelName = ModelDisplayName(response),
            Probabilities = probs,
            Error = response.Error,
        };
    }

    public static CtScanAnalysisPanelDto ToPanel(CheXNetPredictionResponse response) =>
        ToPanel(response, ChestXRayModels.CheXNet);

    public static CtScanAnalysisPanelDto ToPanel(CheXNetPredictionResponse response, string modelId)
    {
        var probs = response.Probabilities ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var topEntry = probs.Count == 0
            ? default(KeyValuePair<string, double>)
            : probs.OrderByDescending(kv => kv.Value).First();
        var topProb = response.Confidence ?? topEntry.Value;
        var predicted = !string.IsNullOrWhiteSpace(response.PredictedClass)
            ? response.PredictedClass!
            : (string.IsNullOrWhiteSpace(topEntry.Key) ? "—" : topEntry.Key);

        return new CtScanAnalysisPanelDto
        {
            PredictedDisease = predicted,
            ConfidenceScore = Math.Round(topProb, 4),
            RiskLevel = MapRiskLevel(topProb),
            ModelName = response.ModelUsed ?? ChestXRayModels.GetDisplayName(modelId),
            Probabilities = probs,
            InferenceMs = response.InferenceMs > 0 ? response.InferenceMs : null,
        };
    }
}
