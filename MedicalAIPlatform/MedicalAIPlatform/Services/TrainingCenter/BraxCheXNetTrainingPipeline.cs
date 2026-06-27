using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class BraxCheXNetTrainingPipeline : IModelTrainingPipeline
{
    private readonly ApplicationDbContext _db;
    private readonly CheXNetPathResolver _paths;
    private readonly PythonScriptRunner _python;
    private readonly TrainingJobRuntimeStore _runtime;
    private readonly BraxTrainingQueueService _queue;
    private readonly TrainingNotificationService _notifications;
    private readonly TrainingQueueService _queueService;
    private readonly DatasetArchiveService _datasetArchive;
    private readonly ILogger<BraxCheXNetTrainingPipeline> _log;

    public BraxCheXNetTrainingPipeline(
        ApplicationDbContext db,
        CheXNetPathResolver paths,
        PythonScriptRunner python,
        TrainingJobRuntimeStore runtime,
        BraxTrainingQueueService queue,
        TrainingNotificationService notifications,
        TrainingQueueService queueService,
        DatasetArchiveService datasetArchive,
        ILogger<BraxCheXNetTrainingPipeline> log)
    {
        _db = db;
        _paths = paths;
        _python = python;
        _runtime = runtime;
        _queue = queue;
        _notifications = notifications;
        _queueService = queueService;
        _datasetArchive = datasetArchive;
        _log = log;
    }

    public string ModelId => ModelTrainingNames.CheXNet;
    public string DisplayName => "CheXNet";
    public string Description => "14-label chest X-ray model (DenseNet121). Fine-tune on BRAX DICOM datasets.";
    public string PipelineKind => "BRAX-Dataset";

    public TrainingConfigDefaultsDto GetConfigDefaults() => new()
    {
        SubsetSize = 100,
        AllowedSubsetSizes = [100, 300, 600, 1000, 3000],
        Epochs = 2,
        LearningRate = 1e-4,
        BatchSize = 8,
        ValRatio = 0.2,
        EarlyStoppingPatience = 0,
        FreezeBackbone = true,
        UnfreezeLastBlock = false,
        NumWorkers = OperatingSystem.IsWindows() ? 0 : 2,
        RandomSeed = 42,
        SaveBestModel = true,
    };

    public async Task<ModelManagementCardDto> BuildCardAsync(CancellationToken ct = default)
    {
        var production = await _db.ModelVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ModelName == ModelId && v.IsProduction, ct).ConfigureAwait(false);
        var lastJob = await _db.TrainingJobs.AsNoTracking()
            .Where(j => j.ModelName == ModelId)
            .OrderByDescending(j => j.FinishedAt ?? j.StartedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var deployable = await _db.ModelVersions.AsNoTracking()
            .CountAsync(v => v.ModelName == ModelId && v.IsDeployable && !v.IsProduction, ct).ConfigureAwait(false);
        var active = _runtime.GetActive();
        var status = active?.ModelId == ModelId ? active.Status : "Idle";

        return new ModelManagementCardDto
        {
            ModelId = ModelId,
            DisplayName = DisplayName,
            Description = Description,
            PipelineKind = PipelineKind,
            CurrentVersion = production?.VersionNumber ?? "1.0",
            Status = status,
            LastTrainingDate = lastJob?.FinishedAt ?? production?.TrainingDate,
            ProductionCheckpoint = production?.FilePath,
            DatasetName = "BRAX",
            DeployStatus = production?.IsProduction == true ? "Production" : "Not deployed",
            ProductionAccuracy = production?.Accuracy,
            ProductionF1 = production?.F1Score,
            ProductionRocAuc = null,
            DeployableVersionsCount = deployable,
            ConfigDefaults = GetConfigDefaults(),
        };
    }

    public async Task<DatasetValidationResultDto> ValidateDatasetAsync(string datasetPath, CancellationToken ct = default)
    {
        var braxRoot = string.IsNullOrWhiteSpace(datasetPath) ? _paths.ResolveBraxRoot() : datasetPath;
        var reportPath = Path.Combine(_paths.ResolveJobStateDirectory(), $"validation_{Guid.NewGuid():N}.json");

        var args = new List<string> { "--brax-root", braxRoot, "--save-report", reportPath };
        var (code, output) = await _python.RunAsync("validate_brax_dataset.py", args, ct).ConfigureAwait(false);

        if (!File.Exists(reportPath))
        {
            return new DatasetValidationResultDto
            {
                Ready = false,
                DatasetPath = braxRoot,
                DatasetName = Path.GetFileName(braxRoot.TrimEnd(Path.DirectorySeparatorChar)),
                Errors = { "Validation script did not produce a report.", output },
            };
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var dataset = root.TryGetProperty("dataset", out var ds) ? ds : default;
        var ready = root.TryGetProperty("ready_for_training", out var r) && r.GetBoolean();

        var errors = ReadStringList(dataset, "errors");
        var warnings = ReadStringList(dataset, "warnings");
        var chexStats = ReadIntDict(dataset, "chexnet_label_statistics");
        var positive = chexStats.Where(kv => kv.Key != "No Finding" && kv.Value > 0).Sum(kv => kv.Value);
        var normal = chexStats.GetValueOrDefault("No Finding");

        long? sizeBytes = null;
        try { sizeBytes = Directory.Exists(braxRoot) ? DirSize(new DirectoryInfo(braxRoot)) : null; } catch { }

        return new DatasetValidationResultDto
        {
            Ready = ready && code == 0,
            DatasetName = Path.GetFileName(braxRoot.TrimEnd(Path.DirectorySeparatorChar)),
            DatasetPath = braxRoot,
            DatasetSizeBytes = sizeBytes,
            TotalRows = dataset.TryGetProperty("total_rows", out var tr) ? tr.GetInt32() : 0,
            ValidRows = dataset.TryGetProperty("valid_rows", out var vr) ? vr.GetInt32() : 0,
            MissingImages = dataset.TryGetProperty("missing_images", out var mi) ? mi.GetInt32() : 0,
            EstimatedPatients = Math.Max(1, (dataset.TryGetProperty("valid_rows", out var v2) ? v2.GetInt32() : 0) / 3),
            PositiveImages = positive,
            NormalImages = normal,
            ValRatio = 0.2,
            DiseaseDistribution = chexStats,
            Errors = errors,
            Warnings = warnings,
            ReportJsonPath = reportPath,
        };
    }

    public Task<TrainingConfirmationDto> BuildConfirmationAsync(TrainingStartRequestDto request, CancellationToken ct = default)
    {
        var ckpt = request.ResumeCheckpoint
                   ?? request.FinetuneCheckpoint
                   ?? _paths.ResolveCheXNetMasterDirectory();
        ckpt = string.IsNullOrWhiteSpace(request.FinetuneCheckpoint)
            ? Path.Combine(_paths.ResolveCheXNetMasterDirectory(), "model.pth.tar")
            : request.FinetuneCheckpoint!;

        var etaMinutes = request.SubsetSize switch
        {
            <= 100 => 5,
            <= 300 => 12,
            <= 600 => 25,
            <= 1000 => 45,
            _ => 120,
        };

        return Task.FromResult(new TrainingConfirmationDto
        {
            Model = DisplayName,
            Dataset = request.DatasetName ?? request.DatasetPath,
            SubsetSize = request.SubsetSize,
            Epochs = request.Epochs,
            LearningRate = request.LearningRate,
            CheckpointSource = File.Exists(ckpt) ? ckpt : "ImageNet backbone (no checkpoint)",
            EstimatedDuration = $"~{etaMinutes} min on CPU (less on GPU)",
            EstimatedRam = request.SubsetSize <= 300 ? "~2–4 GB" : "~4–8 GB",
        });
    }

    public async Task<TrainingStartResponseDto> StartTrainingAsync(TrainingStartRequestDto request, string userId, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid();
        var braxRoot = string.IsNullOrWhiteSpace(request.DatasetPath) ? _paths.ResolveBraxRoot() : request.DatasetPath;
        var hasActive = _runtime.GetActive() is not null
            || await _db.TrainingJobs.AnyAsync(
                j => j.Status == TrainingJobStatuses.Running || j.Status == TrainingJobStatuses.Preparing, ct)
                .ConfigureAwait(false);

        var dbJob = new TrainingJob
        {
            Id = jobId,
            ModelName = ModelId,
            DatasetSize = request.SubsetSize,
            StartedAt = DateTimeOffset.UtcNow,
            Status = hasActive ? TrainingJobStatuses.Queued : TrainingJobStatuses.Pending,
            DatasetPath = braxRoot,
            TrainingBatchId = Guid.NewGuid(),
            RequestedByUserId = userId,
            EpochsConfigured = request.Epochs,
            LearningRate = request.LearningRate,
            BatchSizeConfigured = request.BatchSize,
            ExperimentName = $"{DisplayName}-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            HyperparametersJson = JsonSerializer.Serialize(request),
        };
        _db.TrainingJobs.Add(dbJob);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _queueService.AssignQueuePositionsAsync(ct).ConfigureAwait(false);
        await _datasetArchive.RecordTrainingUseAsync(braxRoot, ModelId, ct).ConfigureAwait(false);

        var runtime = new BraxTrainingJobRuntime
        {
            JobId = jobId,
            ModelId = ModelId,
            Status = dbJob.Status,
            DatasetPath = braxRoot,
            Request = request,
            StartedByUserId = userId,
        };
        _runtime.Register(runtime);
        _queue.Enqueue(jobId);

        await _notifications.NotifyAsync(
            TrainingNotificationTypes.TrainingStarted,
            ModelId,
            hasActive
                ? $"Training queued (position {dbJob.QueuePosition ?? 1})."
                : "Training job started.",
            "info",
            jobId,
            ct).ConfigureAwait(false);

        _log.LogInformation("Queued BRAX training job {JobId} for CheXNet (status={Status})", jobId, dbJob.Status);

        return new TrainingStartResponseDto
        {
            Success = true,
            JobId = jobId,
            Confirmation = await BuildConfirmationAsync(request, ct).ConfigureAwait(false),
        };
    }

    private static List<string> ReadStringList(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
    }

    private static Dictionary<string, int> ReadIntDict(JsonElement parent, string name)
    {
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!parent.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return dict;
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.TryGetInt32(out var v))
                dict[prop.Name] = v;
        }
        return dict;
    }

    private static long DirSize(DirectoryInfo dir)
    {
        long size = 0;
        foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            size += f.Length;
        return size;
    }
}

public sealed class HitlTrainingPipeline : IModelTrainingPipeline
{
    private readonly string _modelId;
    private readonly string _displayName;
    private readonly string _description;
    private readonly ApplicationDbContext _db;
    private readonly ModelRetrainingOrchestrator _retraining;
    private readonly Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.ModelRetrainingOptions> _options;

    public HitlTrainingPipeline(
        string modelId,
        string displayName,
        string description,
        ApplicationDbContext db,
        ModelRetrainingOrchestrator retraining,
        Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.ModelRetrainingOptions> options)
    {
        _modelId = modelId;
        _displayName = displayName;
        _description = description;
        _db = db;
        _retraining = retraining;
        _options = options;
    }

    public string ModelId => _modelId;
    public string DisplayName => _displayName;
    public string Description => _description;
    public string PipelineKind => "HITL-Feedback";

    public TrainingConfigDefaultsDto GetConfigDefaults() => new()
    {
        SubsetSize = _options.Value.ModifiedThreshold,
        Epochs = 3,
        LearningRate = 1e-4,
        BatchSize = 8,
        ValRatio = 0.2,
        NumWorkers = 0,
    };

    public async Task<ModelManagementCardDto> BuildCardAsync(CancellationToken ct = default)
    {
        var (accepted, modified, unprocessed) = await CountHitlAsync(ct).ConfigureAwait(false);
        var production = await _db.ModelVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ModelName == _modelId && v.IsProduction, ct).ConfigureAwait(false);
        var deployable = await _db.ModelVersions.AsNoTracking()
            .CountAsync(v => v.ModelName == _modelId && v.IsDeployable && !v.IsProduction, ct).ConfigureAwait(false);
        var lastJob = await _db.TrainingJobs.AsNoTracking()
            .Where(j => j.ModelName == _modelId)
            .OrderByDescending(j => j.FinishedAt ?? j.StartedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return new ModelManagementCardDto
        {
            ModelId = _modelId,
            DisplayName = _displayName,
            Description = _description,
            PipelineKind = PipelineKind,
            CurrentVersion = production?.VersionNumber ?? "1.0",
            Status = lastJob?.Status ?? "Idle",
            LastTrainingDate = lastJob?.FinishedAt ?? production?.TrainingDate,
            ProductionCheckpoint = production?.FilePath,
            DatasetName = "HITL doctor feedback",
            DeployStatus = production?.IsProduction == true ? "Production" : "Not deployed",
            ProductionAccuracy = production?.Accuracy,
            ProductionF1 = production?.F1Score,
            DeployableVersionsCount = deployable,
            HitlAcceptedCount = accepted,
            HitlModifiedCount = modified,
            HitlRetrainingReady = unprocessed >= _options.Value.GetThreshold(_modelId),
            ConfigDefaults = GetConfigDefaults(),
        };
    }

    public Task<DatasetValidationResultDto> ValidateDatasetAsync(string datasetPath, CancellationToken ct = default)
    {
        return Task.FromResult(new DatasetValidationResultDto
        {
            Ready = true,
            DatasetName = "HITL feedback tables",
            DatasetPath = "SQL Server",
            Warnings = { "HITL datasets are collected from doctor Accept/Modify actions in Analytics." },
        });
    }

    public Task<TrainingConfirmationDto> BuildConfirmationAsync(TrainingStartRequestDto request, CancellationToken ct = default)
    {
        return Task.FromResult(new TrainingConfirmationDto
        {
            Model = _displayName,
            Dataset = "HITL exported JSON",
            SubsetSize = request.SubsetSize,
            Epochs = request.Epochs,
            LearningRate = request.LearningRate,
            CheckpointSource = "Current production weights",
            EstimatedDuration = "~10–30 min",
            EstimatedRam = "~2 GB",
        });
    }

    public async Task<TrainingStartResponseDto> StartTrainingAsync(TrainingStartRequestDto request, string userId, CancellationToken ct = default)
    {
        var (ok, message) = await _retraining.RunRetrainingBatchAsync(_modelId, ct).ConfigureAwait(false);
        return new TrainingStartResponseDto
        {
            Success = ok,
            Error = ok ? null : message,
            Confirmation = await BuildConfirmationAsync(request, ct).ConfigureAwait(false),
        };
    }

    private async Task<(int accepted, int modified, int unprocessed)> CountHitlAsync(CancellationToken ct) =>
        _modelId switch
        {
            ModelTrainingNames.CheXNet => (
                await _db.CheXNetAcceptedData.CountAsync(ct),
                await _db.CheXNetModifiedData.CountAsync(ct),
                await _db.CheXNetModifiedData.CountAsync(x => !x.IsProcessed, ct)),
            ModelTrainingNames.LungCancer => (
                await _db.LungCancerAcceptedData.CountAsync(ct),
                await _db.LungCancerModifiedData.CountAsync(ct),
                await _db.LungCancerModifiedData.CountAsync(x => !x.IsProcessed, ct)),
            ModelTrainingNames.BioBERT => (
                await _db.BioBERTAcceptedData.CountAsync(ct),
                await _db.BioBERTModifiedData.CountAsync(ct),
                await _db.BioBERTModifiedData.CountAsync(x => !x.IsProcessed, ct)),
            _ => (0, 0, 0),
        };
}

public sealed class ModelTrainingPipelineRegistry
{
    private readonly IReadOnlyList<IModelTrainingPipeline> _pipelines;

    public ModelTrainingPipelineRegistry(IEnumerable<IModelTrainingPipeline> pipelines) =>
        _pipelines = pipelines.ToList();

    public IModelTrainingPipeline Get(string modelId) =>
        _pipelines.FirstOrDefault(p => p.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown model: {modelId}");

    public IReadOnlyList<IModelTrainingPipeline> All => _pipelines;
}
