using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services.Dicom;

namespace MedicalAIPlatform.Services;

/// <summary>
/// Routes chest X-ray inference to CheXNet (production) or BRAX RAD-DINO (research) without altering either backend.
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

    public Task<(Dictionary<string, CheXNetPredictionResponse> Map, List<string> Notes)> PredictChestAsync(
        byte[] fileBytes,
        string safeFileName,
        string contentType,
        CancellationToken cancellationToken) =>
        PredictChestAsync(fileBytes, safeFileName, contentType, ChestXRayModels.CheXNet, cancellationToken);

    public async Task<(Dictionary<string, CheXNetPredictionResponse> Map, List<string> Notes)> PredictChestAsync(
        byte[] fileBytes,
        string safeFileName,
        string contentType,
        string xrayModelId,
        CancellationToken cancellationToken)
    {
        var modelId = ChestXRayModels.Normalize(xrayModelId);
        if (string.Equals(modelId, ChestXRayModels.BraxRaddino, StringComparison.Ordinal))
            return await PredictBraxRaddinoAsync(fileBytes, safeFileName, contentType, cancellationToken)
                .ConfigureAwait(false);

        return await PredictCheXNetAsync(fileBytes, safeFileName, contentType, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(Dictionary<string, CheXNetPredictionResponse> Map, List<string> Notes)> PredictCheXNetAsync(
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
            viewModel.ModelUsed ??= ChestXRayModels.CheXNetDisplay;
            viewModel.ModelVersion ??= "Production";

            var notes = pipe.Metadata?.StudyNotes is { Count: > 0 } list
                ? new List<string>(list)
                : new List<string>();
            var map = new Dictionary<string, CheXNetPredictionResponse>(StringComparer.OrdinalIgnoreCase)
            {
                [ChestXRayModels.CheXNet] = viewModel
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

        if (rasterMap.TryGetValue("CheXNet", out var chex))
        {
            chex.ModelUsed ??= ChestXRayModels.CheXNetDisplay;
            chex.ModelVersion ??= "Production";
        }

        return (rasterMap, []);
    }

    private async Task<(Dictionary<string, CheXNetPredictionResponse> Map, List<string> Notes)> PredictBraxRaddinoAsync(
        byte[] fileBytes,
        string safeFileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "X-Ray job: {Model} inference; file={File}, dicom={IsDicom}",
            ChestXRayModels.BraxRaddinoDisplay,
            safeFileName,
            AnalyticsDicomRouting.IsDicomUpload(safeFileName, contentType));

        var map = await _cheXNetApi.PredictRaddinoAsync(
                fileBytes,
                safeFileName,
                string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim(),
                cancellationToken)
            .ConfigureAwait(false);

        var notes = new List<string>
        {
            $"Model: {ChestXRayModels.BraxRaddinoDisplay} ({ChestXRayModels.BraxRaddinoStatus})"
        };

        if (AnalyticsDicomRouting.IsDicomUpload(safeFileName, contentType))
        {
            notes.Add("RAD-DINO inference ran on the uploaded DICOM study file.");
        }

        return (map, notes);
    }
}
