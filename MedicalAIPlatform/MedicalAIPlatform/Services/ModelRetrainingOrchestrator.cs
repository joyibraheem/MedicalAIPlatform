using System.Text.Json;
using System.Text.Json.Serialization;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services;

/// <summary>Exports HITL datasets and triggers Python fine-tuning when thresholds are met.</summary>
public sealed class ModelRetrainingOrchestrator
{
    private static readonly JsonSerializerOptions ExportJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ModelRetrainingOptions> _options;
    private readonly ILogger<ModelRetrainingOrchestrator> _logger;

    public ModelRetrainingOrchestrator(
        IServiceScopeFactory scopeFactory,
        IOptions<ModelRetrainingOptions> options,
        ILogger<ModelRetrainingOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public void QueueRetrainingCheck(string modelName)
    {
        if (!_options.Value.EnableAutoRetraining)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var trainingData = scope.ServiceProvider.GetRequiredService<ModelTrainingDataService>();
                var count = await trainingData.CountUnprocessedModifiedAsync(modelName, CancellationToken.None)
                    .ConfigureAwait(false);
                if (count >= _options.Value.GetThreshold(modelName))
                    await RunRetrainingBatchAsync(modelName, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retraining check failed for {Model}", modelName);
            }
        });
    }

    public async Task<(bool Ok, string Message)> RunRetrainingBatchAsync(string modelName, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var trainingData = scope.ServiceProvider.GetRequiredService<ModelTrainingDataService>();
        var trainingApi = scope.ServiceProvider.GetRequiredService<TrainingApiClient>();
        var opts = _options.Value;

        var unprocessedCount = await trainingData.CountUnprocessedModifiedAsync(modelName, ct).ConfigureAwait(false);
        if (unprocessedCount < opts.GetThreshold(modelName))
            return (false, $"Only {unprocessedCount} modified sample(s); threshold is {opts.GetThreshold(modelName)}.");

        var batchId = Guid.NewGuid();
        var exportRoot = Path.GetFullPath(opts.DatasetExportRoot);
        var batchDir = Path.Combine(exportRoot, modelName, batchId.ToString("N"));
        Directory.CreateDirectory(batchDir);

        var dataset = await BuildExportDatasetAsync(db, modelName, batchId, ct).ConfigureAwait(false);
        if (dataset.Modified.Count == 0)
            return (false, "No modified samples to export.");

        var datasetPath = Path.Combine(batchDir, "dataset.json");
        await File.WriteAllTextAsync(
            datasetPath,
            JsonSerializer.Serialize(dataset, ExportJsonOpts),
            ct).ConfigureAwait(false);

        var production = await db.ModelVersions.AsNoTracking()
            .Where(v => v.ModelName == modelName && v.IsProduction)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var job = new TrainingJob
        {
            Id = Guid.NewGuid(),
            ModelName = modelName,
            DatasetSize = dataset.Modified.Count + dataset.Accepted.Count,
            StartedAt = DateTimeOffset.UtcNow,
            Status = TrainingJobStatuses.Pending,
            PreviousModelVersion = production?.VersionNumber ?? "1.0",
            TrainingBatchId = batchId,
            DatasetPath = datasetPath
        };

        db.TrainingJobs.Add(job);
        await MarkSamplesProcessedAsync(db, modelName, batchId, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        job.Status = TrainingJobStatuses.Running;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var apiResult = await trainingApi.StartTrainingAsync(modelName, datasetPath, ct).ConfigureAwait(false);
        if (apiResult is null || !string.IsNullOrWhiteSpace(apiResult.Error))
        {
            job.Status = TrainingJobStatuses.Failed;
            job.FinishedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = apiResult?.Error ?? "Training API call failed.";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return (false, job.ErrorMessage);
        }

        job.NewModelVersion = apiResult.Version;
        job.Accuracy = apiResult.Accuracy;
        job.F1Score = apiResult.F1Score;
        job.Loss = apiResult.Loss;
        job.DatasetSize = apiResult.DatasetSize > 0 ? apiResult.DatasetSize : job.DatasetSize;
        job.TrainingLogPath = apiResult.TrainingLogPath;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.Status = TrainingJobStatuses.Completed;

        var newVersion = new ModelVersion
        {
            Id = Guid.NewGuid(),
            ModelName = modelName,
            VersionNumber = apiResult.Version,
            TrainingDate = DateTimeOffset.UtcNow,
            DatasetSize = job.DatasetSize,
            Accuracy = apiResult.Accuracy,
            F1Score = apiResult.F1Score,
            Loss = apiResult.Loss,
            FilePath = apiResult.FilePath,
            IsProduction = false,
            IsDeployable = IsBetterModel(production, apiResult),
            TrainingJobId = job.Id
        };

        db.ModelVersions.Add(newVersion);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Retraining completed for {Model}: v{Version}, deployable={Deployable}",
            modelName,
            apiResult.Version,
            newVersion.IsDeployable);

        return (true, $"Training job completed — {modelName} {apiResult.Version}.");
    }

    private static bool IsBetterModel(ModelVersion? production, TrainingApiResult candidate)
    {
        if (production is null)
            return true;

        var prodScore = (production.Accuracy ?? 0) + (production.F1Score ?? 0);
        var newScore = (candidate.Accuracy ?? 0) + (candidate.F1Score ?? 0);
        return newScore > prodScore;
    }

    private static async Task<RetrainingExportDataset> BuildExportDatasetAsync(
        ApplicationDbContext db,
        string modelName,
        Guid batchId,
        CancellationToken ct)
    {
        var dataset = new RetrainingExportDataset
        {
            ModelName = modelName,
            BatchId = batchId,
            ExportedAt = DateTimeOffset.UtcNow
        };

        switch (modelName)
        {
            case ModelTrainingNames.CheXNet:
                dataset.Modified = (await db.CheXNetModifiedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromModified).ToList();
                dataset.Accepted = (await db.CheXNetAcceptedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromAccepted).ToList();
                break;
            case ModelTrainingNames.LungCancer:
                dataset.Modified = (await db.LungCancerModifiedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromModified).ToList();
                dataset.Accepted = (await db.LungCancerAcceptedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromAccepted).ToList();
                break;
            case ModelTrainingNames.BioBERT:
                dataset.Modified = (await db.BioBERTModifiedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromModified).ToList();
                dataset.Accepted = (await db.BioBERTAcceptedData.AsNoTracking()
                    .Where(x => !x.IsProcessed).ToListAsync(ct).ConfigureAwait(false))
                    .Select(RetrainingSampleFromAccepted).ToList();
                break;
        }

        return dataset;
    }

    private static RetrainingExportSample RetrainingSampleFromAccepted(ModelAcceptedDataBase x) => new()
    {
        Id = x.Id,
        InputDataReference = x.InputDataReference,
        SourceDataJson = x.SourceDataJson,
        Prediction = x.Prediction,
        ConfidenceScore = x.ConfidenceScore,
        DoctorId = x.DoctorId,
        PatientId = x.PatientId,
        DicomStudyUid = x.DicomStudyUid,
        CreatedAt = x.CreatedAt,
        SourceFeedbackId = x.SourceFeedbackId
    };

    private static RetrainingExportSample RetrainingSampleFromModified(ModelModifiedDataBase x) => new()
    {
        Id = x.Id,
        InputDataReference = x.SourceDataJson ?? "",
        SourceDataJson = x.SourceDataJson,
        OriginalPrediction = x.OriginalPrediction,
        CorrectedPrediction = x.CorrectedPrediction,
        OriginalConfidence = x.OriginalConfidence,
        DoctorNotes = x.DoctorNotes,
        DoctorId = x.DoctorId,
        PatientId = x.PatientId,
        DicomStudyUid = x.DicomStudyUid,
        CreatedAt = x.CreatedAt,
        SourceFeedbackId = x.SourceFeedbackId
    };

    private static async Task MarkSamplesProcessedAsync(
        ApplicationDbContext db,
        string modelName,
        Guid batchId,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        switch (modelName)
        {
            case ModelTrainingNames.CheXNet:
                await db.CheXNetModifiedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                await db.CheXNetAcceptedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                break;
            case ModelTrainingNames.LungCancer:
                await db.LungCancerModifiedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                await db.LungCancerAcceptedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                break;
            case ModelTrainingNames.BioBERT:
                await db.BioBERTModifiedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                await db.BioBERTAcceptedData.Where(x => !x.IsProcessed).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsProcessed, true)
                    .SetProperty(x => x.ProcessedAt, now)
                    .SetProperty(x => x.TrainingBatchId, batchId), ct).ConfigureAwait(false);
                break;
        }
    }
}

public sealed class RetrainingExportDataset
{
    public string ModelName { get; set; } = "";
    public Guid BatchId { get; set; }
    public DateTimeOffset ExportedAt { get; set; }
    public List<RetrainingExportSample> Accepted { get; set; } = [];
    public List<RetrainingExportSample> Modified { get; set; } = [];
}

public sealed class RetrainingExportSample
{
    public Guid Id { get; set; }
    public string InputDataReference { get; set; } = "";
    public string? SourceDataJson { get; set; }
    public string? Prediction { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? OriginalPrediction { get; set; }
    public string? CorrectedPrediction { get; set; }
    public double? OriginalConfidence { get; set; }
    public string? DoctorNotes { get; set; }
    public string DoctorId { get; set; } = "";
    public int? PatientId { get; set; }
    public string? DicomStudyUid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SourceFeedbackId { get; set; }
}
