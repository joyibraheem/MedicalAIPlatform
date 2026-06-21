using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services.Dicom;

namespace MedicalAIPlatform.Services;

/// <summary>
/// CheXNet chest inference for queued analytics jobs (raster or DICOM), mirroring <see cref="AnalyticsController"/> routing.
/// </summary>
public sealed class AnalyticsXRayInferenceExecutor
{
    private static readonly DicomAggregationMethod DefaultSliceAggregation = DicomAggregationMethod.MaxPooling;

    private readonly CheXNetApiClient _cheXNetApi;
    private readonly DicomInferencePipelineOrchestrator _dicomPipeline;
    private readonly ILogger<AnalyticsXRayInferenceExecutor> _logger;

    public AnalyticsXRayInferenceExecutor(
        CheXNetApiClient cheXNetApi,
        DicomInferencePipelineOrchestrator dicomPipeline,
        ILogger<AnalyticsXRayInferenceExecutor> logger)
    {
        _cheXNetApi = cheXNetApi;
        _dicomPipeline = dicomPipeline;
        _logger = logger;
    }

    public async Task<(Dictionary<string, CheXNetPredictionResponse> Map, List<string> Notes)> PredictChestAsync(
        byte[] fileBytes,
        string safeFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (AnalyticsDicomRouting.IsDicomUpload(safeFileName, contentType))
        {
            _logger.LogInformation(
                "X-Ray job: DICOM chest upload → slice pipeline (CheXNet); file={File}",
                safeFileName);

            await using var dicomMs = new MemoryStream(fileBytes, writable: false);
            var pipe = await _dicomPipeline
                .RunAsync(dicomMs, DefaultSliceAggregation, DicomInferenceBackend.HttpCheXNetChest,
                    cancellationToken)
                .ConfigureAwait(false);

            var viewModel = AnalyticsDicomRouting.ToCheXNetPrediction(pipe);
            var notes = pipe.Metadata?.StudyNotes is { Count: > 0 } list
                ? new List<string>(list)
                : new List<string>();
            var map = new Dictionary<string, CheXNetPredictionResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["CheXNet"] = viewModel
            };
            return (map, notes);
        }

        _logger.LogInformation(
            "X-Ray job: raster chest image → CheXNet HTTP; file={File}",
            safeFileName);

        var rasterMap = await _cheXNetApi.PredictAsync(
                fileBytes,
                safeFileName,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
                models: "CheXNet",
                heatmapClass: null,
                topK: 14,
                cancellationToken)
            .ConfigureAwait(false);

        return (rasterMap, []);
    }
}
