using System.Text.Json;
using System.Threading.Channels;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class BraxTrainingJobExecutor
{
    private readonly PythonScriptRunner _python;
    private readonly CheXNetPathResolver _paths;
    private readonly TrainingJobRuntimeStore _runtime;
    private readonly TrainingMonitorReader _monitor;
    private readonly ApplicationDbContext _db;
    private readonly TrainingNotificationService _notifications;
    private readonly DatasetVersioningService _datasetVersioning;
    private readonly TrainingReportPdfService _reportPdf;
    private readonly TrainingRecommendationService _recommendations;
    private readonly TrainingCenterService _center;
    private readonly ILogger<BraxTrainingJobExecutor> _log;

    public BraxTrainingJobExecutor(
        PythonScriptRunner python,
        CheXNetPathResolver paths,
        TrainingJobRuntimeStore runtime,
        TrainingMonitorReader monitor,
        ApplicationDbContext db,
        TrainingNotificationService notifications,
        DatasetVersioningService datasetVersioning,
        TrainingReportPdfService reportPdf,
        TrainingRecommendationService recommendations,
        TrainingCenterService center,
        ILogger<BraxTrainingJobExecutor> log)
    {
        _python = python;
        _paths = paths;
        _runtime = runtime;
        _monitor = monitor;
        _db = db;
        _notifications = notifications;
        _datasetVersioning = datasetVersioning;
        _reportPdf = reportPdf;
        _recommendations = recommendations;
        _center = center;
        _log = log;
    }

    public async Task ExecuteAsync(Guid jobId, CancellationToken ct)
    {
        var job = _runtime.Get(jobId);
        if (job is null) return;

        var dbJob = await _db.TrainingJobs.FindAsync([jobId], ct).ConfigureAwait(false);
        if (dbJob is null) return;
        if (dbJob.Status == TrainingJobStatuses.Cancelled) return;

        try
        {
            job.Status = TrainingJobStatuses.Running;
            dbJob.Status = TrainingJobStatuses.Running;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var braxRoot = job.DatasetPath ?? _paths.ResolveBraxRoot();
            var req = job.Request;

            if (req.PrepareSubset)
            {
                job.Status = TrainingJobStatuses.Preparing;
                dbJob.Status = TrainingJobStatuses.Preparing;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                var prepArgs = new List<string>
                {
                    "--size", req.SubsetSize.ToString(),
                    "--brax-root", braxRoot,
                    "--seed", req.RandomSeed.ToString(),
                };
                var (prepCode, prepOut) = await _python.RunAsync("prepare_brax_subset.py", prepArgs, ct)
                    .ConfigureAwait(false);
                if (prepCode != 0)
                    throw new InvalidOperationException($"Subset preparation failed:\n{prepOut}");
            }

            var outputDir = Directory.CreateDirectory(
                Path.Combine(_paths.ResolveModelVersionsDirectory(),
                    $"BRAX_job_{jobId:N}_{DateTime.UtcNow:yyyyMMdd_HHmmss}")).FullName;
            job.OutputDir = outputDir;
            job.LogPath = Path.Combine(outputDir, "training.log");
            dbJob.TrainingLogPath = job.LogPath;

            var trainArgs = new List<string>
            {
                "--size", req.SubsetSize.ToString(),
                "--brax-root", braxRoot,
                "--epochs", req.Epochs.ToString(),
                "--batch-size", req.BatchSize.ToString(),
                "--learning-rate", req.LearningRate.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
                "--num-workers", req.NumWorkers.ToString(),
                "--skip-validation",
            };

            if (req.UnfreezeLastBlock)
                trainArgs.Add("--unfreeze-last-block");
            if (req.EarlyStoppingPatience is > 0)
            {
                trainArgs.Add("--early-stopping-patience");
                trainArgs.Add(req.EarlyStoppingPatience.Value.ToString());
            }
            if (!string.IsNullOrWhiteSpace(req.ResumeCheckpoint))
            {
                trainArgs.Add("--resume");
                trainArgs.Add(req.ResumeCheckpoint);
            }
            else if (!string.IsNullOrWhiteSpace(req.FinetuneCheckpoint))
            {
                trainArgs.Add("--finetune-from");
                trainArgs.Add(req.FinetuneCheckpoint);
            }

            job.Status = TrainingJobStatuses.Running;
            dbJob.Status = TrainingJobStatuses.Running;
            var python = _paths.ResolvePythonExecutable();
            var workDir = _paths.ResolveCheXNetMasterDirectory();
            var scriptPath = Path.Combine(workDir, "train_brax.py");
            var argText = string.Join(" ", trainArgs.Select(Quote));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{scriptPath}\" {argText}",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
            await using var logWriter = new StreamWriter(job.LogPath, append: false, encoding: System.Text.Encoding.UTF8);
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) logWriter.WriteLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) logWriter.WriteLine(e.Data); };

            proc.Start();
            job.Process = proc;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            await logWriter.FlushAsync(ct).ConfigureAwait(false);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"Training process exited with code {proc.ExitCode}. See {job.LogPath}");

            var metricsPath = FindMetricsJson(outputDir, workDir);
            if (metricsPath is null)
                throw new InvalidOperationException("Training finished but metrics.json was not found.");

            var metricsDir = Path.GetDirectoryName(metricsPath)!;
            job.OutputDir = metricsDir;
            dbJob.TrainingLogPath = Path.Combine(metricsDir, "training.log");
            if (File.Exists(job.LogPath) && !File.Exists(dbJob.TrainingLogPath))
                File.Copy(job.LogPath, dbJob.TrainingLogPath, overwrite: true);

            var summary = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(metricsPath, ct));
            var finalMetrics = summary.TryGetProperty("final_metrics", out var fm) ? fm : default;
            var bestCheckpoint = summary.TryGetProperty("best_checkpoint", out var bc) ? bc.GetString() : null;
            var trainingTime = summary.TryGetProperty("training_time_seconds", out var tt) ? tt.GetDouble() : (double?)null;

            dbJob.Status = TrainingJobStatuses.Completed;
            dbJob.FinishedAt = DateTimeOffset.UtcNow;
            dbJob.Accuracy = ReadMetric(finalMetrics, "accuracy");
            dbJob.F1Score = ReadMetric(finalMetrics, "f1_micro");
            dbJob.Loss = ReadMetric(finalMetrics, "val_loss")
                ?? (summary.TryGetProperty("best_val_loss", out var bvl) ? bvl.GetDouble() : null);
            dbJob.NewModelVersion = Path.GetFileName(Path.GetDirectoryName(metricsPath) ?? outputDir);
            dbJob.DatasetPath = braxRoot;

            var (datasetVersion, datasetHash) = await _datasetVersioning.AssignVersionAsync(braxRoot, true, ct)
                .ConfigureAwait(false);

            var version = new ModelVersion
            {
                Id = Guid.NewGuid(),
                ModelName = ModelTrainingNames.CheXNet,
                VersionNumber = dbJob.NewModelVersion ?? $"BRAX-{DateTime.UtcNow:yyyyMMdd-HHmm}",
                TrainingDate = DateTimeOffset.UtcNow,
                DatasetSize = req.SubsetSize,
                Accuracy = dbJob.Accuracy,
                F1Score = dbJob.F1Score,
                Loss = dbJob.Loss,
                FilePath = bestCheckpoint ?? metricsPath,
                IsProduction = false,
                IsDeployable = true,
                TrainingJobId = jobId,
                DatasetVersion = datasetVersion,
                DatasetHash = datasetHash,
                ValidationStatus = "Validated",
            };
            _db.ModelVersions.Add(version);

            job.Status = TrainingJobStatuses.Completed;
            job.FinishedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var checkpoints = await _center.ListCheckpointsAsync(null, CancellationToken.None).ConfigureAwait(false);
            var recommendation = _recommendations.BuildRecommendation(jobId, checkpoints);
            var reportPath = await _reportPdf.GenerateAsync(
                dbJob.NewModelVersion!,
                ModelTrainingNames.CheXNet,
                datasetVersion,
                datasetHash,
                recommendation,
                ct).ConfigureAwait(false);
            if (reportPath is not null)
            {
                version.TrainingReportPath = reportPath;
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await _notifications.NotifyAsync(
                TrainingNotificationTypes.CheckpointSaved,
                ModelTrainingNames.CheXNet,
                $"Checkpoint saved: {dbJob.NewModelVersion}",
                "success",
                jobId,
                ct).ConfigureAwait(false);
            await _notifications.NotifyAsync(
                TrainingNotificationTypes.TrainingCompleted,
                ModelTrainingNames.CheXNet,
                $"Training completed. F1={dbJob.F1Score?.ToString("P1") ?? "—"}, Acc={dbJob.Accuracy?.ToString("P1") ?? "—"}",
                "success",
                jobId,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "BRAX training job {JobId} failed", jobId);
            job.Status = TrainingJobStatuses.Failed;
            job.Error = ex.Message;
            job.FinishedAt = DateTimeOffset.UtcNow;
            dbJob.Status = TrainingJobStatuses.Failed;
            dbJob.FinishedAt = DateTimeOffset.UtcNow;
            dbJob.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _notifications.NotifyAsync(
                TrainingNotificationTypes.TrainingFailed,
                ModelTrainingNames.CheXNet,
                ex.Message,
                "danger",
                jobId,
                ct).ConfigureAwait(false);
        }
        finally
        {
            job.Process = null;
        }
    }

    private static string? FindMetricsJson(string preferredDir, string chexNetRoot)
    {
        var direct = Path.Combine(preferredDir, "metrics.json");
        if (File.Exists(direct)) return direct;

        var versionsDir = Path.Combine(chexNetRoot, "model_versions");
        if (!Directory.Exists(versionsDir)) return null;
        return Directory.GetFiles(versionsDir, "metrics.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static double? ReadMetric(JsonElement metrics, string name)
    {
        if (metrics.ValueKind != JsonValueKind.Object) return null;
        if (!metrics.TryGetProperty(name, out var v)) return null;
        return v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    }

    private static string Quote(string arg) =>
        arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}

public sealed class BraxTrainingQueueService : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BraxTrainingQueueService> _log;

    public BraxTrainingQueueService(IServiceScopeFactory scopeFactory, ILogger<BraxTrainingQueueService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public void Enqueue(Guid jobId) => _queue.Writer.TryWrite(jobId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<BraxTrainingJobExecutor>();
                await executor.ExecuteAsync(jobId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Training queue failed for job {JobId}", jobId);
            }
        }
    }
}
