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
    public IReadOnlyList<CtScanSliceInfo> Slices { get; init; } = [];
    public string TempDirectory { get; init; } = "";
    public LungAICtResponse? Analysis { get; set; }
    public int? PatientScanId { get; init; }
    public int? PatientId { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
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
    public PatientMedicalHistory Metadata { get; init; } = new();
    public IReadOnlyList<CtScanSliceInfo> Slices { get; init; } = [];
    public bool HasAnalysis { get; init; }
    public int? PatientScanId { get; init; }
    public int? PatientId { get; init; }
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
}
