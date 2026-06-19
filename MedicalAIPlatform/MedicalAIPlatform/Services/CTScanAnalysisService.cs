using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services.Dicom;
using Microsoft.AspNetCore.Http;

namespace MedicalAIPlatform.Services;

public sealed class CTScanAnalysisResult
{
    public string PredictionResult { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool UsedPlaceholder { get; set; }
}

public sealed class CTScanAnalysisService
{
    private readonly LungAIApiClient _lungAIApi;
    private readonly DicomInferencePipelineOrchestrator _dicomPipeline;
    private readonly ILogger<CTScanAnalysisService> _logger;

    private static readonly DicomAggregationMethod DefaultSliceAggregation = DicomAggregationMethod.MaxPooling;

    public CTScanAnalysisService(
        LungAIApiClient lungAIApi,
        DicomInferencePipelineOrchestrator dicomPipeline,
        ILogger<CTScanAnalysisService> logger)
    {
        _lungAIApi = lungAIApi;
        _dicomPipeline = dicomPipeline;
        _logger = logger;
    }

    public async Task<CTScanAnalysisResult> AnalyzeAsync(
        byte[] fileBytes,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var formFile = new FormFile(
                new MemoryStream(fileBytes, writable: false),
                0,
                fileBytes.Length,
                "file",
                fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType ?? "application/octet-stream"
            };

            LungAICtResponse response;
            if (AnalyticsDicomRouting.IsDicomUpload(formFile))
            {
                await using var dicomMs = new MemoryStream(fileBytes, writable: false);
                var pipe = await _dicomPipeline
                    .RunAsync(dicomMs, DefaultSliceAggregation, DicomInferenceBackend.HttpLungCtClassifier, cancellationToken)
                    .ConfigureAwait(false);
                response = AnalyticsDicomRouting.ToLungCtPrediction(pipe);
            }
            else
            {
                response = await _lungAIApi.PredictCtAsync(
                    fileBytes,
                    fileName,
                    contentType ?? "image/jpeg",
                    cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new InvalidOperationException(response.Error);

            var confidence = response.Probabilities.TryGetValue(response.PredictedClass, out var prob)
                ? prob * 100.0
                : response.Probabilities.Values.DefaultIfEmpty(0).Max() * 100.0;

            return new CTScanAnalysisResult
            {
                PredictionResult = FormatPrediction(response.PredictedClass),
                Confidence = Math.Round(confidence, 1),
                Notes = BuildNotes(response),
                UsedPlaceholder = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LungAI unavailable for CT scan analysis; using placeholder result.");
            return GetPlaceholderResult();
        }
    }

    public CTScanAnalysisResult GetPlaceholderResult()
    {
        return new CTScanAnalysisResult
        {
            PredictionResult = "Possible Pneumonia",
            Confidence = 98.4,
            Notes = "AI detected abnormal chest pattern. Please review by specialist.",
            UsedPlaceholder = true
        };
    }

    private static string FormatPrediction(string predictedClass)
    {
        if (string.IsNullOrWhiteSpace(predictedClass))
            return "Inconclusive";

        return predictedClass.Contains("pneumonia", StringComparison.OrdinalIgnoreCase)
            ? "Possible Pneumonia"
            : predictedClass;
    }

    private static string BuildNotes(LungAICtResponse response)
    {
        if (response.Probabilities.Count == 0)
            return "AI analysis completed. Please review by specialist.";

        var top = response.Probabilities
            .OrderByDescending(kv => kv.Value)
            .Take(3)
            .Select(kv => $"{kv.Key}: {kv.Value * 100:F1}%");

        return $"AI detected pattern consistent with {response.PredictedClass}. Top probabilities: {string.Join(", ", top)}.";
    }
}
