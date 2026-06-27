using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class TrainingCenterService
{
    private readonly ModelTrainingPipelineRegistry _pipelines;
    private readonly CheXNetPathResolver _paths;
    private readonly TrainingJobRuntimeStore _runtime;
    private readonly TrainingMonitorReader _monitor;
    private readonly ApplicationDbContext _db;
    private readonly TrainingQueueService _queueService;
    private readonly TrainingNotificationService _notifications;
    private readonly DatasetArchiveService _datasetArchive;
    private readonly DeploymentHistoryService _deploymentHistory;
    private readonly TrainingExperimentService _experiments;
    private readonly TrainingRecommendationService _recommendations;
    private readonly TrainingResourceMonitorService _resources;
    private readonly TrainingCenterExtensionService _extensions;
    private readonly ModelPluginMetadataRegistry _pluginRegistry;
    private readonly Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.ModelRetrainingOptions> _retrainOptions;

    public TrainingCenterService(
        ModelTrainingPipelineRegistry pipelines,
        CheXNetPathResolver paths,
        TrainingJobRuntimeStore runtime,
        TrainingMonitorReader monitor,
        ApplicationDbContext db,
        TrainingQueueService queueService,
        TrainingNotificationService notifications,
        DatasetArchiveService datasetArchive,
        DeploymentHistoryService deploymentHistory,
        TrainingExperimentService experiments,
        TrainingRecommendationService recommendations,
        TrainingResourceMonitorService resources,
        TrainingCenterExtensionService extensions,
        ModelPluginMetadataRegistry pluginRegistry,
        Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.ModelRetrainingOptions> retrainOptions)
    {
        _pipelines = pipelines;
        _paths = paths;
        _runtime = runtime;
        _monitor = monitor;
        _db = db;
        _queueService = queueService;
        _notifications = notifications;
        _datasetArchive = datasetArchive;
        _deploymentHistory = deploymentHistory;
        _experiments = experiments;
        _recommendations = recommendations;
        _resources = resources;
        _extensions = extensions;
        _pluginRegistry = pluginRegistry;
        _retrainOptions = retrainOptions;
    }

    public async Task<TrainingCenterDashboardDto> BuildDashboardAsync(CancellationToken ct = default)
    {
        var models = new List<ModelManagementCardDto>();
        foreach (var pipeline in _pipelines.All)
            models.Add(await pipeline.BuildCardAsync(ct).ConfigureAwait(false));

        var activeMonitor = BuildActiveMonitor();
        var checkpoints = (await ListCheckpointsAsync(null, ct).ConfigureAwait(false)).ToList();
        TrainingJob? latestJob = null;
        try
        {
            latestJob = await _db.TrainingJobs.AsNoTracking()
                .Where(j => j.Status == TrainingJobStatuses.Completed)
                .OrderByDescending(j => j.FinishedAt)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { /* DB unavailable — dashboard still loads from disk/runtime. */ }

        DashboardHomeSummaryDto? homeSummary = null;
        try
        {
            homeSummary = await _extensions.BuildHomeSummaryAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { homeSummary = new DashboardHomeSummaryDto { ModelCount = _pipelines.All.Count }; }
        var activeJobStatus = activeMonitor is null
            ? latestJob?.Status
            : activeMonitor.Status;

        return new TrainingCenterDashboardDto
        {
            Models = models,
            StoredDatasets = ListStoredDatasets(),
            Checkpoints = checkpoints,
            History = await ListHistoryAsync(ct).ConfigureAwait(false),
            System = BuildSystemStatus(),
            ActiveJob = activeMonitor,
            ModifiedThreshold = _retrainOptions.Value.ModifiedThreshold,
            Queue = await _queueService.ListQueueAsync(ct).ConfigureAwait(false),
            Notifications = await _notifications.ListAsync(30, ct).ConfigureAwait(false),
            UnreadNotificationCount = await _notifications.UnreadCountAsync(ct).ConfigureAwait(false),
            DatasetArchive = await _datasetArchive.ListAsync(ct).ConfigureAwait(false),
            Presets = HyperparameterPresets.All.ToList(),
            Experiments = await _experiments.ListExperimentsAsync(null, ct).ConfigureAwait(false),
            DeploymentHistory = await _deploymentHistory.ListAsync(null, ct).ConfigureAwait(false),
            VersionTimeline = await BuildVersionTimelineAsync(null, ct).ConfigureAwait(false),
            LatestRecommendation = latestJob is not null
                ? _recommendations.BuildRecommendation(latestJob.Id, checkpoints)
                : null,
            LiveResources = _resources.Sample(activeMonitor),
            HomeSummary = homeSummary,
            Workflow = _extensions.BuildWorkflow(latestJob?.DatasetPath, activeJobStatus, checkpoints.Any(c => c.IsProduction)),
            RegisteredPlugins = _pluginRegistry.ListRegisteredPlugins(),
        };
    }

    public async Task<IReadOnlyList<CheckpointRowDto>> ListCheckpointsAsync(string? modelId, CancellationToken ct)
    {
        var rows = new List<CheckpointRowDto>();

        try
        {
            var query = _db.ModelVersions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(modelId))
                query = query.Where(v => v.ModelName == modelId);

            var dbVersions = await query.OrderByDescending(v => v.TrainingDate).Take(100)
                .ToListAsync(ct).ConfigureAwait(false);
            foreach (var v in dbVersions)
                rows.Add(ToCheckpointRow(v));
        }
        catch (OperationCanceledException)
        {
            // Request aborted or query cancelled — still return disk checkpoints below.
        }
        catch
        {
            // Database unavailable, pending migration, or transient failure — use disk checkpoints.
        }

        AppendFilesystemCheckpoints(rows);
        return rows.OrderByDescending(r => r.TrainingDate).ToList();
    }

    private void AppendFilesystemCheckpoints(List<CheckpointRowDto> rows)
    {
        var versionsDir = _paths.ResolveModelVersionsDirectory();
        if (!Directory.Exists(versionsDir)) return;

        foreach (var dir in Directory.GetDirectories(versionsDir).OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            var metricsPath = Path.Combine(dir, "metrics.json");
            if (!File.Exists(metricsPath)) continue;
            var id = Path.GetFileName(dir);
            if (rows.Any(r => r.Id == id || r.FilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))) continue;
            rows.Add(ReadCheckpointFromMetrics(dir, metricsPath));
        }
    }

    public async Task<DeployPreviewDto> BuildDeployPreviewAsync(string modelId, string checkpointId, CancellationToken ct)
    {
        var all = await ListCheckpointsAsync(modelId, ct).ConfigureAwait(false);
        var candidate = all.FirstOrDefault(c => c.Id == checkpointId);
        var current = all.FirstOrDefault(c => c.IsProduction);
        var comparison = CompareCheckpoints(all.Where(c => c.Id == checkpointId || c.IsProduction).ToList());
        return new DeployPreviewDto { CurrentProduction = current, Candidate = candidate, Comparison = comparison };
    }

    public async Task<(bool ok, string message)> DeployCheckpointAsync(
        string modelId,
        string checkpointId,
        string? userId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        var all = await ListCheckpointsAsync(modelId, ct).ConfigureAwait(false);
        var candidate = all.FirstOrDefault(c => c.Id == checkpointId);
        if (candidate is null)
            return (false, "Checkpoint not found.");

        var current = all.FirstOrDefault(c => c.IsProduction);
        var versions = await _db.ModelVersions.Where(v => v.ModelName == modelId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var v in versions)
            v.IsProduction = false;

        var match = versions.FirstOrDefault(v => v.Id.ToString() == checkpointId || v.VersionNumber == candidate.Version);
        if (match is null)
        {
            match = new ModelVersion
            {
                Id = Guid.NewGuid(),
                ModelName = modelId,
                VersionNumber = candidate.Version,
                TrainingDate = candidate.TrainingDate,
                DatasetSize = candidate.SubsetSize ?? 0,
                Accuracy = candidate.Accuracy,
                F1Score = candidate.F1,
                Loss = candidate.ValLoss,
                FilePath = candidate.FilePath,
                IsDeployable = true,
            };
            _db.ModelVersions.Add(match);
        }

        match.IsProduction = true;
        match.IsDeployable = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _deploymentHistory.RecordAsync(
            modelId,
            current?.Version,
            match.VersionNumber,
            checkpointId,
            reason == "Rollback" ? "Rollback" : "Deploy",
            userId,
            reason ?? "Manual deployment",
            ct).ConfigureAwait(false);
        await _notifications.NotifyAsync(
            TrainingNotificationTypes.DeployCompleted,
            modelId,
            $"Production set to {match.VersionNumber}.",
            "success",
            null,
            ct).ConfigureAwait(false);

        return (true, $"Production model updated to {match.VersionNumber}. Inference API unchanged — copy checkpoint manually if needed.");
    }

    public async Task<(bool ok, string message, string? checkpointId)> DeployBestAsync(
        string modelId,
        string metric,
        string? userId,
        CancellationToken ct = default)
    {
        var all = await ListCheckpointsAsync(modelId, ct).ConfigureAwait(false);
        var deployable = all.Where(c => c.IsDeployable && !c.IsProduction).ToList();
        if (deployable.Count == 0)
            return (false, "No deployable checkpoints found.", null);

        CheckpointRowDto? best = metric.ToUpperInvariant() switch
        {
            "F1" => deployable.OrderByDescending(c => c.F1 ?? 0).First(),
            "ACCURACY" => deployable.OrderByDescending(c => c.Accuracy ?? 0).First(),
            "VALIDATION LOSS" or "VAL LOSS" => deployable.OrderBy(c => c.ValLoss ?? double.MaxValue).First(),
            _ => deployable.OrderByDescending(c => c.RocAuc ?? c.F1 ?? c.Accuracy ?? 0).First(),
        };

        var (ok, message) = await DeployCheckpointAsync(
            modelId,
            best.Id,
            userId,
            $"Deploy best by {metric}",
            ct).ConfigureAwait(false);
        return (ok, message, best.Id);
    }

    public async Task<(bool ok, string message)> RollbackAsync(string modelId, string? userId, CancellationToken ct)
    {
        var previous = await _db.ModelVersions.AsNoTracking()
            .Where(v => v.ModelName == modelId && v.IsDeployable && !v.IsProduction)
            .OrderByDescending(v => v.TrainingDate)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (previous is null)
            return (false, "No previous deployable version found.");

        var (ok, message) = await DeployCheckpointAsync(
            modelId,
            previous.Id.ToString(),
            userId,
            "Rollback",
            ct).ConfigureAwait(false);
        if (ok)
        {
            await _notifications.NotifyAsync(
                TrainingNotificationTypes.RollbackCompleted,
                modelId,
                $"Rolled back to {previous.VersionNumber}.",
                "warning",
                null,
                ct).ConfigureAwait(false);
        }
        return (ok, message);
    }

    public List<CheckpointCompareRowDto> CompareCheckpoints(IReadOnlyList<CheckpointRowDto> checkpoints)
    {
        if (checkpoints.Count < 2) return [];
        var metrics = new[] { "Accuracy", "F1", "ROC-AUC", "Val Loss", "Training Time (s)", "Subset Size", "Epochs" };
        var rows = new List<CheckpointCompareRowDto>();
        foreach (var metric in metrics)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? bestId = null;
            double bestNum = double.MinValue;
            foreach (var cp in checkpoints)
            {
                var val = metric switch
                {
                    "Accuracy" => cp.Accuracy?.ToString("P2") ?? "—",
                    "F1" => cp.F1?.ToString("P2") ?? "—",
                    "ROC-AUC" => cp.RocAuc?.ToString("P2") ?? "—",
                    "Val Loss" => cp.ValLoss?.ToString("F4") ?? "—",
                    "Training Time (s)" => cp.TrainingTimeSeconds?.ToString("F0") ?? "—",
                    "Subset Size" => cp.SubsetSize?.ToString() ?? "—",
                    "Epochs" => cp.Epochs?.ToString() ?? "—",
                    _ => "—",
                };
                values[cp.Id] = val;
                var num = metric == "Val Loss" ? -(cp.ValLoss ?? double.MaxValue) : cp.F1 ?? cp.Accuracy ?? 0;
                if (num > bestNum) { bestNum = num; bestId = cp.Id; }
            }
            rows.Add(new CheckpointCompareRowDto { Metric = metric, Values = values, BestCheckpointId = bestId });
        }
        return rows;
    }

    public TrainingChartsDto ReadChartsForJob(Guid jobId)
    {
        var job = _runtime.Get(jobId);
        if (job?.OutputDir != null && Directory.Exists(job.OutputDir))
            return _monitor.ReadCharts(job.OutputDir);

        var dbJob = _db.TrainingJobs.AsNoTracking().FirstOrDefault(j => j.Id == jobId);
        if (dbJob?.TrainingLogPath != null)
        {
            var dir = Path.GetDirectoryName(dbJob.TrainingLogPath);
            if (dir != null && Directory.Exists(dir))
                return _monitor.ReadCharts(dir);
        }

        return new TrainingChartsDto();
    }

    public async Task<(bool ok, string message)> CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var runtime = _runtime.Get(jobId);
        if (runtime?.Process is { HasExited: false } proc)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                runtime.Status = TrainingJobStatuses.Cancelled;
                runtime.FinishedAt = DateTimeOffset.UtcNow;
            }
            catch
            {
                return (false, "Could not cancel running process.");
            }
        }

        var dbJob = await _db.TrainingJobs.FindAsync([jobId], ct).ConfigureAwait(false);
        if (dbJob is null) return (false, "Job not found.");

        if (dbJob.Status is TrainingJobStatuses.Queued or TrainingJobStatuses.Pending)
            return await _queueService.CancelQueuedJobAsync(jobId, ct).ConfigureAwait(false);

        dbJob.Status = TrainingJobStatuses.Cancelled;
        dbJob.FinishedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Job cancelled.");
    }

    public bool CancelJob(Guid jobId) =>
        CancelJobAsync(jobId).GetAwaiter().GetResult().ok;

    public async Task<List<ModelVersionTimelineRowDto>> BuildVersionTimelineAsync(string? modelId, CancellationToken ct)
    {
        var checkpoints = await ListCheckpointsAsync(modelId, ct).ConfigureAwait(false);
        return checkpoints.Select(c => new ModelVersionTimelineRowDto
        {
            Id = c.Id,
            ModelId = c.ModelId,
            VersionNumber = c.Version,
            TrainingDate = c.TrainingDate,
            Dataset = c.Dataset,
            Epochs = c.Epochs,
            Accuracy = c.Accuracy,
            F1 = c.F1,
            RocAuc = c.RocAuc,
            DeploymentStatus = c.IsProduction ? "Production" : c.Status,
        }).ToList();
    }

    public LiveResourceMonitorDto SampleResources() => _resources.Sample(BuildActiveMonitor());

    private LiveTrainingMonitorDto? BuildActiveMonitor()
    {
        var active = _runtime.GetActive();
        return active is null ? null : _monitor.ReadLive(active);
    }

    private List<StoredDatasetOptionDto> ListStoredDatasets()
    {
        var list = new List<StoredDatasetOptionDto>();
        var brax = _paths.ResolveBraxRoot();
        if (Directory.Exists(brax))
        {
            list.Add(new StoredDatasetOptionDto
            {
                Id = "brax-default",
                Name = "BRAX (datasets/BRAX)",
                Path = brax,
                Kind = "BRAX",
            });
        }

        var uploadRoot = _paths.ResolveDatasetUploadDirectory();
        foreach (var dir in Directory.GetDirectories(uploadRoot))
        {
            list.Add(new StoredDatasetOptionDto
            {
                Id = Path.GetFileName(dir),
                Name = Path.GetFileName(dir),
                Path = dir,
                Kind = "Uploaded",
            });
        }

        return list;
    }

    private async Task<List<TrainingHistoryRowDto>> ListHistoryAsync(CancellationToken ct)
    {
        var jobs = await _db.TrainingJobs.AsNoTracking()
            .OrderByDescending(j => j.StartedAt)
            .Take(50)
            .ToListAsync(ct).ConfigureAwait(false);

        return jobs.Select(j => new TrainingHistoryRowDto
        {
            JobId = j.Id,
            ModelId = j.ModelName,
            StartedAt = j.StartedAt,
            FinishedAt = j.FinishedAt,
            Duration = j.FinishedAt.HasValue ? (j.FinishedAt.Value - j.StartedAt).ToString(@"hh\:mm\:ss") : "—",
            Dataset = j.DatasetPath ?? "—",
            Status = j.Status,
            FailureReason = j.ErrorMessage,
            CheckpointPath = j.NewModelVersion,
            LogPath = j.TrainingLogPath,
            Hyperparameters = j.HyperparametersJson ?? $"size={j.DatasetSize}, ep={j.EpochsConfigured}, lr={j.LearningRate}",
            User = j.RequestedByUserId,
        }).ToList();
    }

    private SystemStatusDto BuildSystemStatus()
    {
        var chexRoot = _paths.ResolveCheXNetMasterDirectory();
        var versionsDir = _paths.ResolveModelVersionsDirectory();
        var drive = new DriveInfo(Path.GetPathRoot(chexRoot) ?? "C:\\");

        return new SystemStatusDto
        {
            CpuUsagePercent = 0,
            RamUsedGb = GC.GetTotalMemory(false) / (1024.0 * 1024 * 1024),
            RamTotalGb = 0,
            DiskFreeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024),
            DiskTotalGb = drive.TotalSize / (1024.0 * 1024 * 1024),
            DatasetStorageGb = Directory.Exists(_paths.ResolveBraxRoot())
                ? DirSizeGb(_paths.ResolveBraxRoot())
                : 0,
            CheckpointCount = Directory.Exists(versionsDir)
                ? Directory.GetDirectories(versionsDir).Length
                : 0,
            PythonVersion = "See training venv",
            TorchVersion = "—",
            CudaAvailable = false,
        };
    }

    private static double DirSizeGb(string path)
    {
        long bytes = 0;
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            bytes += f.Length;
        return bytes / (1024.0 * 1024 * 1024);
    }

    private static CheckpointRowDto ToCheckpointRow(ModelVersion v) => new()
    {
        Id = v.Id.ToString(),
        ModelId = v.ModelName,
        Version = v.VersionNumber,
        TrainingDate = v.TrainingDate,
        Dataset = "HITL/BRAX",
        SubsetSize = v.DatasetSize,
        Accuracy = v.Accuracy,
        F1 = v.F1Score,
        ValLoss = v.Loss,
        Status = v.IsProduction ? "Production" : v.IsDeployable ? "Deployable" : "Archived",
        FilePath = v.FilePath,
        IsProduction = v.IsProduction,
        IsDeployable = v.IsDeployable,
        TrainingJobId = v.TrainingJobId,
        IsFavorite = v.IsFavorite,
        IsPinned = v.IsPinned,
        IsProductionCandidate = v.IsProductionCandidate,
        IsRecommended = v.IsRecommended,
        DatasetVersion = v.DatasetVersion,
        DatasetHash = v.DatasetHash,
        ValidationStatus = v.ValidationStatus,
        Notes = v.Notes,
        ReportPath = v.TrainingReportPath,
    };

    private CheckpointRowDto ReadCheckpointFromMetrics(string dir, string metricsPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metricsPath));
        var root = doc.RootElement;
        var final = root.TryGetProperty("final_metrics", out var fm) ? fm : default;
        var id = Path.GetFileName(dir);
        ApplySidecarFlags(dir, out var flags, out var notes);
        return new CheckpointRowDto
        {
            Id = id,
            ModelId = ModelTrainingNames.CheXNet,
            Version = id,
            TrainingDate = File.GetLastWriteTimeUtc(dir),
            Dataset = "BRAX",
            SubsetSize = root.TryGetProperty("train_samples", out var ts) ? ts.GetInt32() + (root.TryGetProperty("val_samples", out var vs) ? vs.GetInt32() : 0) : null,
            Epochs = root.TryGetProperty("epochs", out var ep) ? ep.GetInt32() : null,
            Accuracy = ReadD(final, "accuracy"),
            F1 = ReadD(final, "f1_micro"),
            RocAuc = ReadD(final, "roc_auc_macro"),
            ValLoss = root.TryGetProperty("best_val_loss", out var bvl) ? bvl.GetDouble() : ReadD(final, "val_loss"),
            TrainingTimeSeconds = root.TryGetProperty("training_time_seconds", out var tt) ? tt.GetDouble() : null,
            Status = "Deployable",
            FilePath = root.TryGetProperty("best_checkpoint", out var bc) ? bc.GetString() ?? dir : dir,
            IsDeployable = true,
            IsFavorite = flags.IsFavorite,
            IsPinned = flags.IsPinned,
            IsProductionCandidate = flags.IsProductionCandidate,
            IsRecommended = flags.IsRecommended,
            Notes = notes,
            ReportPath = File.Exists(Path.Combine(_paths.ResolveCheXNetMasterDirectory(), "reports", "training", $"{id}_report.pdf"))
                ? Path.Combine(_paths.ResolveCheXNetMasterDirectory(), "reports", "training", $"{id}_report.pdf")
                : null,
        };
    }

    private static void ApplySidecarFlags(string dir, out CheckpointFlagsDto flags, out string? notes)
    {
        flags = new CheckpointFlagsDto();
        notes = null;
        var sidecar = Path.Combine(dir, "checkpoint_meta.json");
        if (!File.Exists(sidecar)) return;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(sidecar));
            var root = doc.RootElement;
            flags = new CheckpointFlagsDto
            {
                IsFavorite = root.TryGetProperty("isFavorite", out var f) && f.GetBoolean(),
                IsPinned = root.TryGetProperty("isPinned", out var p) && p.GetBoolean(),
                IsProductionCandidate = root.TryGetProperty("isProductionCandidate", out var pc) && pc.GetBoolean(),
                IsRecommended = root.TryGetProperty("isRecommended", out var r) && r.GetBoolean(),
            };
            if (root.TryGetProperty("notes", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String)
                notes = n.GetString();
        }
        catch { }
    }

    private static double? ReadD(System.Text.Json.JsonElement el, string name) =>
        el.ValueKind == System.Text.Json.JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
            ? v.GetDouble()
            : null;
}
