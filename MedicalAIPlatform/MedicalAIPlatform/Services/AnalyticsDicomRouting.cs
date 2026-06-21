using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using Microsoft.AspNetCore.Http;

namespace MedicalAIPlatform.Services;

/// <summary>Detects radiology uploads for the slice-based DICOM pipeline and maps outputs to Analytics view models.</summary>
public static class AnalyticsDicomRouting
{
    public static bool IsDicomUpload(IFormFile? file)
    {
        if (file == null || file.Length == 0) return false;
        return IsDicomUpload(file.FileName, file.ContentType);
    }

    /// <summary>Detect DICOM uploads using filename extension / MIME (for buffered uploads without <see cref="IFormFile"/>).</summary>
    public static bool IsDicomUpload(string? fileName, string? contentType)
    {
        if (!string.IsNullOrEmpty(fileName) && HasDicomExtension(fileName.AsSpan()))
            return true;

        var ct = (contentType ?? string.Empty).Trim();
        return ContentTypeLooksLikeDicom(ct);
    }

    private static bool HasDicomExtension(ReadOnlySpan<char> fileName)
    {
        var i = fileName.LastIndexOf('.');
        if (i < 0 || i >= fileName.Length - 1) return false;

        ReadOnlySpan<char> ext = fileName[(i + 1)..];
        return ext.Equals("dcm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals("dicm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContentTypeLooksLikeDicom(string ct)
    {
        if (string.IsNullOrEmpty(ct)) return false;

        if (string.Equals(ct, "application/dicom", StringComparison.OrdinalIgnoreCase))
            return true;
        if (ct.StartsWith("application/dicom;", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(ct, "application/x-dicom", StringComparison.OrdinalIgnoreCase))
            return true;
        return string.Equals(ct, "image/dicom", StringComparison.OrdinalIgnoreCase);
    }

    public static CheXNetPredictionResponse ToCheXNetPrediction(DicomInferencePipelineResponse pipe)
    {
        var probs = new Dictionary<string, double>(pipe.FinalPrediction, StringComparer.Ordinal);
        var top = probs
            .OrderByDescending(kv => kv.Value)
            .Take(14)
            .Select(kv => new CheXNetTopKItem { ClassName = kv.Key, Probability = kv.Value })
            .ToList();

        var hmB64 = pipe.VisualizationHeatmapBase64;
        var hmMime = string.IsNullOrWhiteSpace(pipe.VisualizationHeatmapMime) ? "image/png" : pipe.VisualizationHeatmapMime!;
        var heatmap = string.IsNullOrWhiteSpace(hmB64)
            ? new CheXNetHeatmap()
            : new CheXNetHeatmap
            {
                ClassName = pipe.VisualizationHeatmapClass ?? "",
                Mime = hmMime,
                ImageBase64 = hmB64,
            };

        var prevB64 = pipe.VisualizationPreviewBase64;
        var prevMime = string.IsNullOrWhiteSpace(pipe.VisualizationPreviewMime)
            ? "image/jpeg"
            : pipe.VisualizationPreviewMime;

        return new CheXNetPredictionResponse
        {
            ClassNames = top.Select(t => t.ClassName).Distinct(StringComparer.Ordinal).ToArray(),
            Probabilities = probs,
            TopK = top,
            Heatmap = heatmap,
            PreviewImageMime = prevMime ?? "image/jpeg",
            PreviewImageBase64 = prevB64 ?? "",
            PneumoniaProbability = InferPneumoniaProbability(probs),
            Device = PipeDeviceLabel(pipe, "slice-CheXNet"),
            InferenceMs = ClampMs(pipe.PipelineStats?.TotalInferenceMs),
        };
    }

    public static LungAICtResponse ToLungCtPrediction(DicomInferencePipelineResponse pipe)
    {
        var probs = new Dictionary<string, double>(pipe.FinalPrediction, StringComparer.OrdinalIgnoreCase);
        var predicted = probs.Count == 0
            ? string.Empty
            : probs.OrderByDescending(kv => kv.Value).First().Key;

        var prevB64 = pipe.VisualizationPreviewBase64;
        var prevMime = string.IsNullOrWhiteSpace(pipe.VisualizationPreviewMime)
            ? "image/jpeg"
            : pipe.VisualizationPreviewMime;

        return new LungAICtResponse
        {
            PredictedClass = predicted,
            ClassNames = probs.Keys.OrderByDescending(k => probs[k]).ToArray(),
            Probabilities = probs,
            ScanType = "CT-DICOM",
            Error = null,
            PreviewImageMime = prevMime ?? "image/jpeg",
            PreviewImageBase64 = prevB64 ?? "",
        };
    }

    private static string PipeDeviceLabel(DicomInferencePipelineResponse pipe, string fallback)
    {
        if (pipe.PipelineStats is not { InferenceBackend.Length: > 0 })
            return fallback;

        var agg = string.IsNullOrEmpty(pipe.AggregationMethod) ? "" : pipe.AggregationMethod;
        return $"DICOM-{agg}+{pipe.PipelineStats.InferenceBackend}";
    }

    private static int ClampMs(long? ms) =>
        ms.HasValue ? (int)Math.Clamp(ms.Value, 0, int.MaxValue) : 0;

    private static double InferPneumoniaProbability(Dictionary<string, double> probs)
    {
        foreach (var kv in probs)
        {
            if (string.Equals(kv.Key, "Pneumonia", StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        foreach (var kv in probs)
        {
            if (kv.Key.Contains("Pneumonia", StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return 0;
    }
}
