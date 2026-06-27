using System.IO.Compression;
using System.Text.Json;
using MedicalAIPlatform.Models.TrainingCenter;
using MedicalAIPlatform.Services.TrainingCenter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Controllers;

[Authorize(Roles = "Admin")]
[Route("Admin/TrainingCenter")]
public sealed class AdminTrainingCenterController : Controller
{
    private readonly TrainingCenterService _center;
    private readonly ModelTrainingPipelineRegistry _pipelines;
    private readonly CheXNetPathResolver _paths;
    private readonly TrainingJobRuntimeStore _runtime;
    private readonly TrainingMonitorReader _monitor;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TrainingNotificationService _notifications;
    private readonly DatasetArchiveService _datasetArchive;
    private readonly TrainingQueueService _queueService;
    private readonly TrainingExperimentService _experiments;
    private readonly TrainingInferenceTestService _inferenceTest;
    private readonly TrainingRecommendationService _recommendations;
    private readonly DeploymentHistoryService _deploymentHistory;
    private readonly TrainingCenterExtensionService _extensions;
    private readonly ModelPluginMetadataRegistry _pluginRegistry;
    private readonly BraxDatasetCsvService _datasetCsv;
    private readonly TrainingThresholdAnalyzerService _thresholdAnalyzer;
    private readonly CheckpointMetadataService _checkpointMeta;
    private readonly ProductionIntegrationTestService _productionTest;
    private readonly TrainingReportPdfService _reportPdf;

    public AdminTrainingCenterController(
        TrainingCenterService center,
        ModelTrainingPipelineRegistry pipelines,
        CheXNetPathResolver paths,
        TrainingJobRuntimeStore runtime,
        TrainingMonitorReader monitor,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        TrainingNotificationService notifications,
        DatasetArchiveService datasetArchive,
        TrainingQueueService queueService,
        TrainingExperimentService experiments,
        TrainingInferenceTestService inferenceTest,
        TrainingRecommendationService recommendations,
        DeploymentHistoryService deploymentHistory,
        TrainingCenterExtensionService extensions,
        ModelPluginMetadataRegistry pluginRegistry,
        BraxDatasetCsvService datasetCsv,
        TrainingThresholdAnalyzerService thresholdAnalyzer,
        CheckpointMetadataService checkpointMeta,
        ProductionIntegrationTestService productionTest,
        TrainingReportPdfService reportPdf)
    {
        _center = center;
        _pipelines = pipelines;
        _paths = paths;
        _runtime = runtime;
        _monitor = monitor;
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
        _datasetArchive = datasetArchive;
        _queueService = queueService;
        _experiments = experiments;
        _inferenceTest = inferenceTest;
        _recommendations = recommendations;
        _deploymentHistory = deploymentHistory;
        _extensions = extensions;
        _pluginRegistry = pluginRegistry;
        _datasetCsv = datasetCsv;
        _thresholdAnalyzer = thresholdAnalyzer;
        _checkpointMeta = checkpointMeta;
        _productionTest = productionTest;
        _reportPdf = reportPdf;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var dto = await _center.BuildDashboardAsync(ct).ConfigureAwait(false);
        return Json(dto);
    }

    [HttpPost("validate-dataset")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateDataset([FromForm] string modelId, [FromForm] string? datasetPath, CancellationToken ct)
    {
        var pipeline = _pipelines.Get(modelId);
        var path = ResolveDatasetPath(datasetPath);
        var result = await pipeline.ValidateDatasetAsync(path, ct).ConfigureAwait(false);
        await _datasetArchive.RegisterOrUpdateAsync(path, result.DatasetName, result, ct).ConfigureAwait(false);
        await _notifications.NotifyAsync(
            result.Ready ? TrainingNotificationTypes.DatasetValidationPassed : TrainingNotificationTypes.DatasetValidationFailed,
            modelId,
            result.Ready ? $"Dataset {result.DatasetName} passed validation." : $"Validation failed for {result.DatasetName}.",
            result.Ready ? "success" : "danger",
            null,
            ct).ConfigureAwait(false);
        return Json(result);
    }

    [HttpPost("upload-dataset")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_147_483_648)]
    public async Task<IActionResult> UploadDataset(IFormFile? zipFile, CancellationToken ct)
    {
        if (zipFile is null || zipFile.Length == 0)
            return BadRequest(new { error = "Select a ZIP file to upload." });

        var uploadRoot = _paths.ResolveDatasetUploadDirectory();
        var folderName = $"upload_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..8]}";
        var dest = Directory.CreateDirectory(Path.Combine(uploadRoot, folderName)).FullName;
        var zipPath = Path.Combine(dest, zipFile.FileName);

        await using (var fs = System.IO.File.Create(zipPath))
            await zipFile.CopyToAsync(fs, ct).ConfigureAwait(false);

        ZipFile.ExtractToDirectory(zipPath, dest);
        var archived = await _datasetArchive.RegisterOrUpdateAsync(dest, folderName, null, ct).ConfigureAwait(false);
        return Json(new StoredDatasetOptionDto
        {
            Id = folderName,
            Name = archived.Name,
            Path = dest,
            Kind = "Uploaded",
        });
    }

    [HttpPost("confirm-training")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmTraining([FromBody] TrainingStartRequestDto request, CancellationToken ct)
    {
        var pipeline = _pipelines.Get(request.ModelId);
        var confirmation = await pipeline.BuildConfirmationAsync(request, ct).ConfigureAwait(false);
        return Json(confirmation);
    }

    [HttpPost("start-training")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTraining([FromBody] TrainingStartRequestDto request, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (user is null) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.DatasetPath))
            request.DatasetPath = ResolveDatasetPath(request.DatasetPath);

        var pipeline = _pipelines.Get(request.ModelId);
        var result = await pipeline.StartTrainingAsync(request, user.Id, ct).ConfigureAwait(false);
        return result.Success ? Json(result) : BadRequest(result);
    }

    [HttpGet("monitor/active")]
    public IActionResult ActiveMonitor()
    {
        var active = _runtime.GetActive();
        if (active is null) return Json(new { active = false });
        var dto = _monitor.ReadLive(active);
        return Json(new { active = true, monitor = dto });
    }

    [HttpGet("monitor/{jobId:guid}")]
    public IActionResult Monitor(Guid jobId)
    {
        var job = _runtime.Get(jobId);
        if (job is null)
            return NotFound(new { error = "Job not found in runtime store." });
        return Json(_monitor.ReadLive(job));
    }

    [HttpPost("cancel/{jobId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid jobId, CancellationToken ct)
    {
        var (ok, message) = await _center.CancelJobAsync(jobId, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue(CancellationToken ct) =>
        Json(await _queueService.ListQueueAsync(ct).ConfigureAwait(false));

    [HttpPost("queue/cancel/{jobId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelQueued(Guid jobId, CancellationToken ct)
    {
        var (ok, message) = await _queueService.CancelQueuedJobAsync(jobId, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct) =>
        Json(await _notifications.ListAsync(100, ct).ConfigureAwait(false));

    [HttpPost("notifications/read/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, ct).ConfigureAwait(false);
        return Json(new { success = true });
    }

    [HttpPost("notifications/read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(ct).ConfigureAwait(false);
        return Json(new { success = true });
    }

    [HttpPost("notifications/clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearNotifications(CancellationToken ct)
    {
        await _notifications.ClearAllAsync(ct).ConfigureAwait(false);
        return Json(new { success = true });
    }

    [HttpGet("dataset-archive")]
    public async Task<IActionResult> DatasetArchive(CancellationToken ct) =>
        Json(await _datasetArchive.ListAsync(ct).ConfigureAwait(false));

    [HttpDelete("dataset-archive/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDatasetArchive(Guid id, CancellationToken ct)
    {
        var (ok, message) = await _datasetArchive.DeleteAsync(id, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpGet("presets")]
    public IActionResult Presets() => Json(HyperparameterPresets.All);

    [HttpGet("experiments")]
    public async Task<IActionResult> Experiments(string? modelId, CancellationToken ct) =>
        Json(await _experiments.ListExperimentsAsync(modelId, ct).ConfigureAwait(false));

    [HttpPost("experiments/compare")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareExperiments([FromBody] List<Guid> jobIds, CancellationToken ct)
    {
        var all = await _experiments.ListExperimentsAsync(null, ct).ConfigureAwait(false);
        var selected = all.Where(e => jobIds.Contains(e.JobId)).ToList();
        return Json(_experiments.CompareExperiments(selected));
    }

    [HttpPost("deploy-best")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeployBest([FromBody] DeployBestRequestDto request, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        var (ok, message, checkpointId) = await _center.DeployBestAsync(
            request.ModelId, request.Metric, user?.Id, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message, checkpointId }) : BadRequest(new { error = message });
    }

    [HttpGet("deployment-history")]
    public async Task<IActionResult> DeploymentHistory(string? modelId, CancellationToken ct) =>
        Json(await _deploymentHistory.ListAsync(modelId, ct).ConfigureAwait(false));

    [HttpGet("version-timeline")]
    public async Task<IActionResult> VersionTimeline(string? modelId, CancellationToken ct) =>
        Json(await _center.BuildVersionTimelineAsync(modelId, ct).ConfigureAwait(false));

    [HttpGet("recommendation/{jobId:guid}")]
    public async Task<IActionResult> Recommendation(Guid jobId, CancellationToken ct)
    {
        var checkpoints = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        return Json(_recommendations.BuildRecommendation(jobId, checkpoints));
    }

    [HttpGet("live-resources")]
    public IActionResult LiveResources() => Json(_center.SampleResources());

    [HttpPost("inference-test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InferenceTest(
        IFormFile? image,
        [FromForm] string source,
        [FromForm] string? checkpointPath,
        [FromForm] string? candidateCheckpointPath,
        [FromForm] string? heatmapClass,
        CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "Upload an image." });

        await using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();

        if (source.Equals("compare", StringComparison.OrdinalIgnoreCase))
        {
            var compare = await _inferenceTest.CompareAsync(
                bytes, image.FileName, image.ContentType ?? "image/jpeg",
                candidateCheckpointPath, heatmapClass, ct).ConfigureAwait(false);
            return Json(compare);
        }

        var result = await _inferenceTest.RunSingleAsync(
            bytes, image.FileName, image.ContentType ?? "image/jpeg",
            source, checkpointPath, heatmapClass, ct).ConfigureAwait(false);
        return Json(result);
    }

    [HttpGet("charts/{jobId:guid}")]
    public IActionResult Charts(Guid jobId)
    {
        var charts = _center.ReadChartsForJob(jobId);
        return Json(charts);
    }

    [HttpGet("checkpoints")]
    public async Task<IActionResult> Checkpoints(string? modelId, CancellationToken ct)
    {
        var rows = await _center.ListCheckpointsAsync(modelId, ct).ConfigureAwait(false);
        return Json(rows);
    }

    [HttpPost("compare")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Compare([FromBody] CheckpointCompareRequestDto request, CancellationToken ct)
    {
        var all = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        var selected = all.Where(c => request.CheckpointIds.Contains(c.Id)).ToList();
        return Json(_center.CompareCheckpoints(selected));
    }

    [HttpGet("deploy-preview")]
    public async Task<IActionResult> DeployPreview(string modelId, string checkpointId, CancellationToken ct)
    {
        var preview = await _center.BuildDeployPreviewAsync(modelId, checkpointId, ct).ConfigureAwait(false);
        return Json(preview);
    }

    [HttpPost("deploy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deploy([FromBody] DeployCheckpointRequestDto request, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        var (ok, message) = await _center.DeployCheckpointAsync(
            request.ModelId, request.CheckpointId, user?.Id, "Manual deployment", ct)
            .ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpPost("rollback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rollback([FromForm] string modelId, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        var (ok, message) = await _center.RollbackAsync(modelId, user?.Id, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpDelete("checkpoint/{checkpointId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCheckpoint(string checkpointId, CancellationToken ct)
    {
        var version = await _db.ModelVersions.FirstOrDefaultAsync(
            v => v.Id.ToString() == checkpointId || v.VersionNumber == checkpointId, ct).ConfigureAwait(false);

        if (version?.IsProduction == true)
            return BadRequest(new { error = "Cannot delete the production checkpoint." });

        if (version is not null)
        {
            _db.ModelVersions.Remove(version);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        var versionsDir = _paths.ResolveModelVersionsDirectory();
        var dir = Path.Combine(versionsDir, checkpointId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);

        return Json(new { success = true });
    }

    [HttpGet("download/{checkpointId}")]
    public async Task<IActionResult> Download(string checkpointId, CancellationToken ct)
    {
        var all = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        var cp = all.FirstOrDefault(c => c.Id == checkpointId);
        if (cp is null) return NotFound();

        var path = cp.FilePath;
        if (Directory.Exists(path))
        {
            var zipPath = Path.Combine(Path.GetTempPath(), $"checkpoint_{checkpointId}_{Guid.NewGuid():N}.zip");
            if (System.IO.File.Exists(zipPath)) System.IO.File.Delete(zipPath);
            ZipFile.CreateFromDirectory(path, zipPath);
            return PhysicalFile(zipPath, "application/zip", $"{checkpointId}.zip");
        }

        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "Checkpoint file not found on disk." });

        return PhysicalFile(path, "application/octet-stream", Path.GetFileName(path));
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var dto = await _center.BuildDashboardAsync(ct).ConfigureAwait(false);
        return Json(dto.History);
    }

    [HttpGet("system-status")]
    public async Task<IActionResult> SystemStatus(CancellationToken ct)
    {
        var dto = await _center.BuildDashboardAsync(ct).ConfigureAwait(false);
        return Json(dto.System);
    }

    [HttpGet("home-summary")]
    public async Task<IActionResult> HomeSummary(CancellationToken ct) =>
        Json(await _extensions.BuildHomeSummaryAsync(ct).ConfigureAwait(false));

    [HttpGet("wizard")]
    public IActionResult Wizard(
        string? modelId,
        string? datasetPath,
        bool validated = false,
        bool started = false,
        bool completed = false,
        bool deployed = false) =>
        Json(_extensions.BuildWizardState(modelId, ResolveDatasetPath(datasetPath), validated, started, completed, deployed));

    [HttpGet("workflow")]
    public async Task<IActionResult> Workflow(CancellationToken ct)
    {
        var dashboard = await _center.BuildDashboardAsync(ct).ConfigureAwait(false);
        return Json(dashboard.Workflow);
    }

    [HttpGet("dataset-preview")]
    public async Task<IActionResult> DatasetPreview(string datasetPath, string? modelId, CancellationToken ct)
    {
        var path = ResolveDatasetPath(datasetPath);
        var provider = !string.IsNullOrWhiteSpace(modelId) ? _pluginRegistry.Get(modelId) : null;
        if (provider is not null)
        {
            var preview = await provider.BuildDatasetPreviewAsync(path, ct).ConfigureAwait(false);
            if (preview is not null) return Json(preview);
        }
        return Json(_datasetCsv.BuildPreview(path));
    }

    [HttpGet("dataset-statistics")]
    public async Task<IActionResult> DatasetStatistics(string datasetPath, string? modelId, CancellationToken ct)
    {
        var path = ResolveDatasetPath(datasetPath);
        var provider = !string.IsNullOrWhiteSpace(modelId) ? _pluginRegistry.Get(modelId) : null;
        if (provider is not null)
        {
            var stats = await provider.BuildDatasetStatisticsAsync(path, ct).ConfigureAwait(false);
            if (stats is not null) return Json(stats);
        }
        return Json(_datasetCsv.BuildStatistics(path));
    }

    [HttpPost("dataset-explorer")]
    [ValidateAntiForgeryToken]
    public IActionResult DatasetExplorer([FromBody] DatasetExplorerQueryDto query)
    {
        query.DatasetPath = ResolveDatasetPath(query.DatasetPath);
        return Json(_datasetCsv.Explore(query));
    }

    [HttpGet("dataset-versions")]
    public async Task<IActionResult> DatasetVersions(CancellationToken ct) =>
        Json(await _extensions.ListDatasetVersionsAsync(ct).ConfigureAwait(false));

    [HttpGet("architecture/{modelId}")]
    public IActionResult Architecture(string modelId)
    {
        var arch = _pluginRegistry.GetArchitecture(modelId);
        return arch is null ? NotFound(new { error = "Model plugin not found." }) : Json(arch);
    }

    [HttpGet("plugins")]
    public IActionResult Plugins() => Json(_pluginRegistry.ListRegisteredPlugins());

    [HttpGet("console")]
    public IActionResult ConsoleOutput(Guid? jobId) => Json(_extensions.ReadConsole(jobId));

    [HttpGet("console/{jobId:guid}")]
    public IActionResult ConsoleOutputByJob(Guid jobId) => Json(_extensions.ReadConsole(jobId));

    [HttpGet("threshold/{checkpointId}")]
    public async Task<IActionResult> Threshold(string checkpointId, double threshold = 0.5, CancellationToken ct = default)
    {
        try
        {
            return Json(await _thresholdAnalyzer.AnalyzeAsync(checkpointId, threshold, ct).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("checkpoint-details/{checkpointId}")]
    public async Task<IActionResult> CheckpointDetails(string checkpointId, CancellationToken ct)
    {
        var all = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        var details = await _extensions.GetCheckpointDetailsAsync(checkpointId, all, ct).ConfigureAwait(false);
        return details is null ? NotFound(new { error = "Checkpoint not found." }) : Json(details);
    }

    [HttpPost("checkpoint-flags")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCheckpointFlags([FromBody] CheckpointFlagsUpdateDto dto, CancellationToken ct)
    {
        var (ok, message) = await _checkpointMeta.UpdateFlagsAsync(dto, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpPost("checkpoint-notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCheckpointNotes([FromBody] CheckpointNotesUpdateDto dto, CancellationToken ct)
    {
        var (ok, message) = await _checkpointMeta.UpdateNotesAsync(dto, ct).ConfigureAwait(false);
        return ok ? Json(new { success = true, message }) : BadRequest(new { error = message });
    }

    [HttpGet("checkpoint-artifact/{checkpointId}/{fileName}")]
    public async Task<IActionResult> DownloadArtifact(string checkpointId, string fileName, CancellationToken ct)
    {
        var all = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        var details = await _extensions.GetCheckpointDetailsAsync(checkpointId, all, ct).ConfigureAwait(false);
        if (details is null || !details.DownloadLinks.TryGetValue(fileName, out var path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "Artifact not found." });
        return PhysicalFile(path, "application/octet-stream", fileName);
    }

    [HttpGet("report/{checkpointId}")]
    public async Task<IActionResult> DownloadReport(string checkpointId, CancellationToken ct)
    {
        var version = await _db.ModelVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id.ToString() == checkpointId || v.VersionNumber == checkpointId, ct)
            .ConfigureAwait(false);
        var reportPath = version?.TrainingReportPath;
        if (string.IsNullOrEmpty(reportPath) || !System.IO.File.Exists(reportPath))
        {
            var fallback = Path.Combine(_paths.ResolveCheXNetMasterDirectory(), "reports", "training", $"{checkpointId}_report.pdf");
            reportPath = System.IO.File.Exists(fallback) ? fallback : null;
        }
        if (reportPath is null) return NotFound(new { error = "Report not found." });
        return PhysicalFile(reportPath, "application/pdf", $"{checkpointId}_report.pdf");
    }

    [HttpGet("enhanced-recommendation/{jobId:guid}")]
    public async Task<IActionResult> EnhancedRecommendation(Guid jobId, CancellationToken ct)
    {
        var checkpoints = await _center.ListCheckpointsAsync(null, ct).ConfigureAwait(false);
        return Json(_extensions.BuildEnhancedRecommendation(jobId, checkpoints));
    }

    [HttpPost("production-test")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProductionTest(IFormFile? image, CancellationToken ct)
    {
        if (image is null || image.Length == 0)
            return BadRequest(new { error = "Upload an image." });
        await using var ms = new MemoryStream();
        await image.CopyToAsync(ms, ct).ConfigureAwait(false);
        var result = await _productionTest.RunAsync(
            ms.ToArray(), image.FileName, image.ContentType ?? "image/jpeg", ct).ConfigureAwait(false);
        return Json(result);
    }

    private string ResolveDatasetPath(string? datasetPath)
    {
        if (string.IsNullOrWhiteSpace(datasetPath) || datasetPath == "brax-default")
            return _paths.ResolveBraxRoot();

        if (Path.IsPathRooted(datasetPath) && Directory.Exists(datasetPath))
            return datasetPath;

        var uploadRoot = _paths.ResolveDatasetUploadDirectory();
        var candidate = Path.Combine(uploadRoot, datasetPath.Trim());
        if (Directory.Exists(candidate))
            return candidate;

        return datasetPath;
    }
}
