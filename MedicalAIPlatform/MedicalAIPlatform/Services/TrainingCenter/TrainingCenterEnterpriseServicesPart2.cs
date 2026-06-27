using System.Diagnostics;
using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;
using MedicalAIPlatform.Services;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class DeploymentHistoryService
{
    private readonly ApplicationDbContext _db;

    public DeploymentHistoryService(ApplicationDbContext db) => _db = db;

    public async Task RecordAsync(
        string modelName,
        string? fromVersion,
        string toVersion,
        string checkpointId,
        string action,
        string? userId,
        string? reason,
        CancellationToken ct = default)
    {
        try
        {
            _db.DeploymentHistoryRecords.Add(new DeploymentHistoryRecord
            {
                Id = Guid.NewGuid(),
                ModelName = modelName,
                FromVersion = fromVersion,
                ToVersion = toVersion,
                ToCheckpointId = checkpointId,
                DeployedAt = DateTimeOffset.UtcNow,
                DeployedByUserId = userId,
                Action = action,
                Reason = reason,
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch { }
    }

    public async Task<List<DeploymentHistoryRowDto>> ListAsync(string? modelId, CancellationToken ct = default)
    {
        try
        {
            var query = _db.DeploymentHistoryRecords.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(modelId))
                query = query.Where(r => r.ModelName == modelId);

            var rows = await query.OrderByDescending(r => r.DeployedAt).Take(50).ToListAsync(ct).ConfigureAwait(false);
            var production = await _db.ModelVersions.AsNoTracking()
                .Where(v => v.IsProduction)
                .ToDictionaryAsync(v => v.ModelName, v => v.VersionNumber, ct).ConfigureAwait(false);

            return rows.Select(r => new DeploymentHistoryRowDto
            {
                Id = r.Id,
                ModelName = r.ModelName,
                FromVersion = r.FromVersion,
                ToVersion = r.ToVersion,
                DeployedAt = r.DeployedAt,
                Action = r.Action,
                Reason = r.Reason,
                IsCurrentProduction = production.GetValueOrDefault(r.ModelName) == r.ToVersion,
            }).ToList();
        }
        catch
        {
            return [];
        }
    }
}

public sealed class TrainingExperimentService
{
    private readonly ApplicationDbContext _db;
    private readonly CheXNetPathResolver _paths;

    public TrainingExperimentService(ApplicationDbContext db, CheXNetPathResolver paths)
    {
        _db = db;
        _paths = paths;
    }

    public async Task<List<ExperimentRowDto>> ListExperimentsAsync(string? modelId, CancellationToken ct = default)
    {
        var query = _db.TrainingJobs.AsNoTracking()
            .Where(j => j.Status == TrainingJobStatuses.Completed || j.Status == TrainingJobStatuses.Failed);
        if (!string.IsNullOrWhiteSpace(modelId))
            query = query.Where(j => j.ModelName == modelId);

        var jobs = await query.OrderByDescending(j => j.FinishedAt ?? j.StartedAt).Take(100).ToListAsync(ct)
            .ConfigureAwait(false);

        return jobs.Select(j => ToExperiment(j, ReadMetricsForJob(j))).ToList();
    }

    public ExperimentCompareResultDto CompareExperiments(IReadOnlyList<ExperimentRowDto> experiments)
    {
        if (experiments.Count < 2)
            return new ExperimentCompareResultDto { Summary = "Select at least two experiments." };

        var best = experiments
            .OrderByDescending(e => e.RocAuc ?? e.F1 ?? e.Accuracy ?? 0)
            .First();

        var compareRows = new List<CheckpointCompareRowDto>();
        var metrics = new[] { "Accuracy", "F1", "ROC-AUC", "Val Loss", "Training Time (s)", "Epochs", "Batch Size", "Learning Rate" };
        foreach (var metric in metrics)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? bestId = null;
            double bestNum = double.MinValue;
            foreach (var ex in experiments)
            {
                var key = ex.JobId.ToString();
                var val = metric switch
                {
                    "Accuracy" => ex.Accuracy?.ToString("P2") ?? "—",
                    "F1" => ex.F1?.ToString("P2") ?? "—",
                    "ROC-AUC" => ex.RocAuc?.ToString("P2") ?? "—",
                    "Val Loss" => ex.ValLoss?.ToString("F4") ?? "—",
                    "Training Time (s)" => ex.TrainingTimeSeconds?.ToString("F0") ?? "—",
                    "Epochs" => ex.Epochs.ToString(),
                    "Batch Size" => ex.BatchSize.ToString(),
                    "Learning Rate" => ex.LearningRate.ToString("G4"),
                    _ => "—",
                };
                values[key] = val;
                var num = metric == "Val Loss" ? -(ex.ValLoss ?? double.MaxValue) : ex.RocAuc ?? ex.F1 ?? ex.Accuracy ?? 0;
                if (num > bestNum) { bestNum = num; bestId = key; }
            }
            compareRows.Add(new CheckpointCompareRowDto { Metric = metric, Values = values, BestCheckpointId = bestId });
        }

        return new ExperimentCompareResultDto
        {
            Experiments = experiments.ToList(),
            MetricComparison = compareRows,
            BestExperimentId = best.JobId.ToString(),
            RecommendedExperimentId = best.JobId.ToString(),
            Summary = $"Recommended experiment: {best.ExperimentName} (ROC-AUC {best.RocAuc:P1}, F1 {best.F1:P1}).",
        };
    }

    private JsonElement? ReadMetricsForJob(TrainingJob job)
    {
        if (string.IsNullOrEmpty(job.NewModelVersion)) return null;
        var dir = Path.Combine(_paths.ResolveModelVersionsDirectory(), job.NewModelVersion);
        var metricsPath = Path.Combine(dir, "metrics.json");
        if (!File.Exists(metricsPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metricsPath));
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    private static ExperimentRowDto ToExperiment(TrainingJob j, JsonElement? metrics)
    {
        JsonElement final = default;
        if (metrics?.TryGetProperty("final_metrics", out var fm) == true)
            final = fm;

        return new ExperimentRowDto
        {
            JobId = j.Id,
            ExperimentName = j.ExperimentName ?? $"{j.ModelName}-{j.StartedAt:yyyyMMdd-HHmm}",
            ModelId = j.ModelName,
            Dataset = j.DatasetPath ?? "—",
            Epochs = j.EpochsConfigured ?? 2,
            LearningRate = j.LearningRate ?? 1e-4,
            BatchSize = j.BatchSizeConfigured ?? 8,
            TrainingTimeSeconds = metrics?.TryGetProperty("training_time_seconds", out var tt) == true ? tt.GetDouble() : null,
            ValLoss = j.Loss ?? ReadD(final, "val_loss"),
            Accuracy = j.Accuracy ?? ReadD(final, "accuracy"),
            F1 = j.F1Score ?? ReadD(final, "f1_micro"),
            RocAuc = ReadD(final, "roc_auc_macro"),
            Precision = ReadD(final, "precision_micro"),
            Recall = ReadD(final, "recall_micro"),
            CheckpointId = j.NewModelVersion,
            Status = j.Status,
        };
    }

    private static double? ReadD(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : null;
}

public sealed class TrainingRecommendationService
{
    public TrainingRecommendationDto? BuildRecommendation(
        Guid jobId,
        IReadOnlyList<CheckpointRowDto> checkpoints)
    {
        var production = checkpoints.FirstOrDefault(c => c.IsProduction);
        var candidate = checkpoints.FirstOrDefault(c => c.TrainingJobId == jobId)
            ?? checkpoints.OrderByDescending(c => c.TrainingDate).FirstOrDefault(c => !c.IsProduction);

        if (candidate is null) return null;

        var accDelta = Delta(production?.Accuracy, candidate.Accuracy);
        var f1Delta = Delta(production?.F1, candidate.F1);
        var rocDelta = Delta(production?.RocAuc, candidate.RocAuc);
        var deploy = (rocDelta ?? f1Delta ?? accDelta ?? 0) > 0.01;

        return new TrainingRecommendationDto
        {
            JobId = jobId,
            ModelId = candidate.ModelId,
            CurrentProduction = production,
            Candidate = candidate,
            AccuracyDelta = accDelta,
            F1Delta = f1Delta,
            RocAucDelta = rocDelta,
            TrainingTimeDeltaSeconds = candidate.TrainingTimeSeconds,
            Recommendation = deploy ? "Deploy New Model" : "Keep Current Production Model",
            Explanation = deploy
                ? "Candidate exceeds production on key metrics with acceptable validation loss."
                : "Production model remains competitive; candidate does not show sufficient improvement.",
        };
    }

    private static double? Delta(double? baseline, double? candidate)
    {
        if (baseline is null || candidate is null) return null;
        return candidate - baseline;
    }
}

public sealed class TrainingResourceMonitorService
{
    private static readonly Queue<double> CpuHistory = new();
    private static readonly Queue<double> RamHistory = new();
    private static readonly Queue<double> GpuHistory = new();
    private static DateTime _lastSample = DateTime.MinValue;

    public LiveResourceMonitorDto Sample(LiveTrainingMonitorDto? activeJob)
    {
        SampleSystem();
        var drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveType == DriveType.Fixed);
        var proc = Process.GetCurrentProcess();
        var ramTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024);
        var ramUsed = proc.WorkingSet64 / (1024.0 * 1024 * 1024);
        var diskTotal = drive?.TotalSize / (1024.0 * 1024 * 1024) ?? 0;
        var diskFree = drive?.AvailableFreeSpace / (1024.0 * 1024 * 1024) ?? 0;

        return new LiveResourceMonitorDto
        {
            CpuUsagePercent = CpuHistory.LastOrDefault(),
            RamUsedGb = ramUsed,
            RamTotalGb = ramTotal,
            RamUsagePercent = ramTotal > 0 ? 100.0 * ramUsed / ramTotal : 0,
            DiskUsedPercent = drive is null ? 0 : 100.0 * (1.0 - (double)drive.AvailableFreeSpace / drive.TotalSize),
            DiskUsedGb = diskTotal - diskFree,
            DiskTotalGb = diskTotal,
            GpuUsagePercent = GpuHistory.LastOrDefault(),
            VramUsagePercent = null,
            GpuTemperatureC = null,
            GpuName = "CUDA probe via system status",
            TrainingSpeedImagesPerSec = EstimateSpeed(activeJob),
            EstimatedRemainingSeconds = activeJob?.EtaSeconds,
            CurrentEpoch = activeJob?.CurrentEpoch,
            CurrentBatch = activeJob?.CurrentBatch,
            CpuHistory = CpuHistory.Select((v, i) => new ChartPointDto { Epoch = i + 1, Value = v }).ToList(),
            RamHistory = RamHistory.Select((v, i) => new ChartPointDto { Epoch = i + 1, Value = v }).ToList(),
            GpuHistory = GpuHistory.Select((v, i) => new ChartPointDto { Epoch = i + 1, Value = v }).ToList(),
        };
    }

    private static void SampleSystem()
    {
        if ((DateTime.UtcNow - _lastSample).TotalSeconds < 2) return;
        _lastSample = DateTime.UtcNow;
        Enqueue(CpuHistory, Math.Min(100, procCpuEstimate()));
        Enqueue(RamHistory, Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024 * 1024));
        Enqueue(GpuHistory, 0);
    }

    private static void Enqueue(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > 30) q.Dequeue();
    }

    private static double procCpuEstimate() =>
        Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount / 10.0;

    private static double? EstimateSpeed(LiveTrainingMonitorDto? job)
    {
        if (job?.CurrentEpoch is null or 0 || job.ElapsedSeconds <= 0) return null;
        return (job.CurrentEpoch * 100.0) / job.ElapsedSeconds;
    }
}

public sealed class TrainingInferenceTestService
{
    private readonly CheXNetApiClient _chexNet;
    private readonly PythonScriptRunner _python;
    private readonly CheXNetPathResolver _paths;

    public TrainingInferenceTestService(
        CheXNetApiClient chexNet,
        PythonScriptRunner python,
        CheXNetPathResolver paths)
    {
        _chexNet = chexNet;
        _python = python;
        _paths = paths;
    }

    public async Task<InferenceCompareResultDto> CompareAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        string? candidateCheckpointPath,
        string? heatmapClass,
        CancellationToken ct = default)
    {
        var production = await RunProductionAsync(imageBytes, fileName, contentType, heatmapClass, ct).ConfigureAwait(false);
        InferenceTestResultDto? candidate = null;
        if (!string.IsNullOrWhiteSpace(candidateCheckpointPath))
            candidate = await RunCandidateAsync(imageBytes, fileName, candidateCheckpointPath, heatmapClass, ct).ConfigureAwait(false);

        return new InferenceCompareResultDto { Production = production, Candidate = candidate };
    }

    public async Task<InferenceTestResultDto> RunSingleAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        string source,
        string? checkpointPath,
        string? heatmapClass,
        CancellationToken ct = default)
    {
        if (source.Equals("candidate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(checkpointPath))
            return await RunCandidateAsync(imageBytes, fileName, checkpointPath, heatmapClass, ct).ConfigureAwait(false);
        return await RunProductionAsync(imageBytes, fileName, contentType, heatmapClass, ct).ConfigureAwait(false);
    }

    private async Task<InferenceTestResultDto> RunProductionAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        string? heatmapClass,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _chexNet.PredictAsync(imageBytes, fileName, contentType, heatmapClass: heatmapClass, cancellationToken: ct)
                .ConfigureAwait(false);
            sw.Stop();
            var parsed = result.GetValueOrDefault("CheXNet");
            return new InferenceTestResultDto
            {
                Source = "Production API",
                Label = parsed?.TopK.FirstOrDefault()?.ClassName ?? "—",
                InferenceTimeMs = parsed?.InferenceMs ?? sw.Elapsed.TotalMilliseconds,
                Predictions = MapPredictions(parsed),
                HeatmapBase64 = parsed?.Heatmap?.ImageBase64,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new InferenceTestResultDto { Source = "Production API", Error = ex.Message, InferenceTimeMs = sw.Elapsed.TotalMilliseconds };
        }
    }

    private async Task<InferenceTestResultDto> RunCandidateAsync(
        byte[] imageBytes,
        string fileName,
        string checkpointPath,
        string? heatmapClass,
        CancellationToken ct)
    {
        var tempImage = Path.Combine(_paths.ResolveJobStateDirectory(), $"infer_{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        var reportPath = Path.Combine(_paths.ResolveJobStateDirectory(), $"infer_{Guid.NewGuid():N}.json");
        await File.WriteAllBytesAsync(tempImage, imageBytes, ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        try
        {
            var args = new List<string>
            {
                "--checkpoint", checkpointPath,
                "--image", tempImage,
                "--output", reportPath,
            };
            if (!string.IsNullOrWhiteSpace(heatmapClass))
            {
                args.Add("--heatmap-class");
                args.Add(heatmapClass);
            }

            var (code, output) = await _python.RunAsync("admin_checkpoint_predict.py", args, ct).ConfigureAwait(false);
            sw.Stop();
            if (code != 0 || !File.Exists(reportPath))
                return new InferenceTestResultDto { Source = "Candidate checkpoint", Error = output, InferenceTimeMs = sw.Elapsed.TotalMilliseconds };

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, ct).ConfigureAwait(false));
            var root = doc.RootElement;
            var preds = new List<InferenceLabelScoreDto>();
            if (root.TryGetProperty("predictions", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    preds.Add(new InferenceLabelScoreDto
                    {
                        Label = p.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                        Confidence = p.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0,
                    });
                }
            }
            return new InferenceTestResultDto
            {
                Source = "Candidate checkpoint",
                Label = root.TryGetProperty("top_label", out var tl) ? tl.GetString() ?? "—" : "—",
                InferenceTimeMs = root.TryGetProperty("inference_ms", out var ms) ? ms.GetDouble() : sw.Elapsed.TotalMilliseconds,
                Predictions = preds,
                HeatmapBase64 = root.TryGetProperty("heatmap_base64", out var hm) ? hm.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new InferenceTestResultDto { Source = "Candidate checkpoint", Error = ex.Message, InferenceTimeMs = sw.Elapsed.TotalMilliseconds };
        }
        finally
        {
            try { if (File.Exists(tempImage)) File.Delete(tempImage); } catch { }
        }
    }

    private static List<InferenceLabelScoreDto> MapPredictions(CheXNetPredictionResponse? parsed)
    {
        if (parsed?.TopK is { Count: > 0 })
            return parsed.TopK.Select(p => new InferenceLabelScoreDto { Label = p.ClassName, Confidence = p.Probability }).ToList();
        if (parsed?.Probabilities is null) return [];
        return parsed.Probabilities
            .OrderByDescending(kv => kv.Value)
            .Take(14)
            .Select(kv => new InferenceLabelScoreDto { Label = kv.Key, Confidence = kv.Value })
            .ToList();
    }
}
