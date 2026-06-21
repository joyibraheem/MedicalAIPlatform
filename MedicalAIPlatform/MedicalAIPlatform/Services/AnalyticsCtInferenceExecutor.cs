using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services.Dicom;

namespace MedicalAIPlatform.Services;

/// <summary>Runs Chest CT inference from raw bytes (raster slice or DICOM volume).</summary>
public sealed class AnalyticsCtInferenceExecutor
{
    private static readonly DicomAggregationMethod DefaultSliceAggregation = DicomAggregationMethod.MaxPooling;

    private readonly LungAIApiClient _lungAIApi;
    private readonly DicomInferencePipelineOrchestrator _dicomPipeline;
    private readonly ILogger<AnalyticsCtInferenceExecutor> _logger;

    public AnalyticsCtInferenceExecutor(
        LungAIApiClient lungAIApi,
        DicomInferencePipelineOrchestrator dicomPipeline,
        ILogger<AnalyticsCtInferenceExecutor> logger)
    {
        _lungAIApi = lungAIApi;
        _dicomPipeline = dicomPipeline;
        _logger = logger;
    }

    /// <param name="safeFileName">Sanitized filename (for logs / LungAI multipart).</param>
    public async Task<LungAICtResponse> PredictCtAsync(
        byte[] fileBytes,
        string safeFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (AnalyticsDicomRouting.IsDicomUpload(safeFileName, contentType))
        {
            _logger.LogInformation(
                "Processing DICOM CT upload → slice-based pipeline (ChestAI per slice); file={File}",
                safeFileName);

            await using var dicomMs = new MemoryStream(fileBytes, writable: false);
            var pipe = await _dicomPipeline
                .RunAsync(dicomMs, DefaultSliceAggregation, DicomInferenceBackend.HttpLungCtClassifier, cancellationToken)
                .ConfigureAwait(false);

            return AnalyticsDicomRouting.ToLungCtPrediction(pipe);
        }

        _logger.LogInformation(
            "Processing raster CT slice → LungAI /predict/ct; file={File}",
            safeFileName);

        return await _lungAIApi.PredictCtAsync(
            fileBytes,
            safeFileName,
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            cancellationToken).ConfigureAwait(false);
    }
}
