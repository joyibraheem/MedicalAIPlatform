using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class BraxDatasetCsvService
{
    private static readonly string[] SpreadsheetNames = ["master_spreadsheet.csv", "master_spreadsheet_update.csv"];
    private static readonly string[] BraxLabelColumns =
    [
        "No Finding", "Enlarged Cardiomediastinum", "Cardiomegaly", "Lung Lesion", "Lung Opacity",
        "Edema", "Consolidation", "Pneumonia", "Atelectasis", "Pneumothorax", "Pleural Effusion",
        "Pleural Other", "Fracture", "Support Devices",
    ];

    public string? FindSpreadsheet(string datasetPath)
    {
        foreach (var name in SpreadsheetNames)
        {
            var path = Path.Combine(datasetPath, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public string ComputeContentHash(string datasetPath)
    {
        var spreadsheet = FindSpreadsheet(datasetPath);
        if (spreadsheet is null) return ComputePathHash(datasetPath);
        using var sha = SHA256.Create();
        var bytes = File.ReadAllBytes(spreadsheet);
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    private List<BraxCsvRow> ReadAllRows(string datasetPath)
    {
        var spreadsheet = FindSpreadsheet(datasetPath);
        if (spreadsheet is null) return [];

        var lines = File.ReadAllLines(spreadsheet);
        if (lines.Length <= 1) return [];

        var headers = ParseCsvLine(lines[0]);
        var colMap = BuildColumnMap(headers);
        var rows = new List<BraxCsvRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var parts = ParseCsvLine(lines[i]);
            rows.Add(MapRow(parts, colMap, i));
        }
        return rows;
    }

    public DatasetPreviewDto BuildPreview(string datasetPath, string? versionLabel = null, string? validationStatus = null)
    {
        var rows = ReadAllRows(datasetPath);
        var spreadsheet = FindSpreadsheet(datasetPath);
        var contentHash = ComputeContentHash(datasetPath);
        var diseaseDist = ComputeDiseaseDistribution(rows);
        var normal = rows.Count(r => r.IsNormal);
        var positive = rows.Count - normal;
        var patients = rows.Select(r => r.PatientId).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return new DatasetPreviewDto
        {
            Name = Path.GetFileName(datasetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Version = versionLabel ?? InferVersionLabel(datasetPath),
            Path = datasetPath,
            SizeBytes = DirSize(datasetPath),
            ImageCount = rows.Count,
            PatientCount = patients,
            NormalCount = normal,
            PositiveCount = positive,
            DiseaseCount = diseaseDist.Count(kv => kv.Value > 0),
            CreatedDate = spreadsheet is not null ? File.GetCreationTimeUtc(spreadsheet) : Directory.GetCreationTimeUtc(datasetPath),
            ValidationStatus = validationStatus ?? "Unknown",
            ContentHash = contentHash,
            SampleRows = rows.Take(12).Select(ToPreviewRow).ToList(),
        };
    }

    public DatasetStatisticsDto BuildStatistics(string datasetPath, int subsetSize = 100, double valRatio = 0.2)
    {
        var rows = ReadAllRows(datasetPath);
        var diseaseDist = ComputeDiseaseDistribution(rows);
        var normal = rows.Count(r => r.IsNormal);
        var positive = rows.Count - normal;
        var perPatient = rows.GroupBy(r => r.PatientId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Count())
            .OrderByDescending(c => c)
            .Take(20)
            .Select((c, i) => new ChartPointDto { Epoch = i + 1, Value = c })
            .ToList();

        var trainCount = (int)Math.Round(rows.Count * (1 - valRatio));
        var valCount = rows.Count - trainCount;
        var labelFreq = diseaseDist
            .OrderByDescending(kv => kv.Value)
            .Select((kv, i) => new ChartPointDto { Epoch = i + 1, Value = kv.Value })
            .ToList();
        var maxFreq = labelFreq.Count > 0 ? labelFreq.Max(p => p.Value) : 1;
        var minFreq = labelFreq.Count > 0 ? labelFreq.Where(p => p.Value > 0).Select(p => p.Value).DefaultIfEmpty(1).Min() : 1;
        var imbalance = minFreq > 0 ? (double)maxFreq / minFreq : 0;

        return new DatasetStatisticsDto
        {
            DiseaseDistribution = diseaseDist,
            NormalCount = normal,
            PositiveCount = positive,
            ImagesPerPatient = perPatient,
            TrainVsValidation =
            [
                new ChartPointDto { Epoch = 1, Value = trainCount },
                new ChartPointDto { Epoch = 2, Value = valCount },
            ],
            LabelFrequency = labelFreq,
            ClassImbalanceRatio = imbalance,
            SummaryCards =
            [
                new SummaryCardDto { Label = "Total Images", Value = rows.Count.ToString(CultureInfo.InvariantCulture) },
                new SummaryCardDto { Label = "Patients", Value = rows.Select(r => r.PatientId).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString(CultureInfo.InvariantCulture) },
                new SummaryCardDto { Label = "Normal", Value = normal.ToString(CultureInfo.InvariantCulture) },
                new SummaryCardDto { Label = "Positive", Value = positive.ToString(CultureInfo.InvariantCulture) },
                new SummaryCardDto { Label = "Diseases", Value = diseaseDist.Count(kv => kv.Value > 0).ToString(CultureInfo.InvariantCulture) },
                new SummaryCardDto { Label = "Imbalance Ratio", Value = imbalance.ToString("F1", CultureInfo.InvariantCulture) },
            ],
        };
    }

    public DatasetExplorerResultDto Explore(DatasetExplorerQueryDto query)
    {
        var rows = ReadAllRows(query.DatasetPath);
        IEnumerable<BraxCsvRow> filtered = rows;

        if (!string.IsNullOrWhiteSpace(query.PatientId))
            filtered = filtered.Where(r => r.PatientId.Contains(query.PatientId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Study))
            filtered = filtered.Where(r => r.StudyId.Contains(query.Study, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Disease))
            filtered = filtered.Where(r => r.ActiveLabels.Any(l => l.Contains(query.Disease!, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(query.Image))
            filtered = filtered.Where(r => r.ImageName.Contains(query.Image, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Label))
            filtered = filtered.Where(r => r.LabelsText.Contains(query.Label, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Date))
            filtered = filtered.Where(r => r.StudyId.Contains(query.Date, StringComparison.OrdinalIgnoreCase));

        filtered = query.SortBy?.ToUpperInvariant() switch
        {
            "STUDYID" or "STUDY" => query.SortDesc ? filtered.OrderByDescending(r => r.StudyId) : filtered.OrderBy(r => r.StudyId),
            "IMAGENAME" or "IMAGE" => query.SortDesc ? filtered.OrderByDescending(r => r.ImageName) : filtered.OrderBy(r => r.ImageName),
            "LABELS" => query.SortDesc ? filtered.OrderByDescending(r => r.LabelsText) : filtered.OrderBy(r => r.LabelsText),
            _ => query.SortDesc ? filtered.OrderByDescending(r => r.PatientId) : filtered.OrderBy(r => r.PatientId),
        };

        var list = filtered.ToList();
        var page = list.Skip(Math.Max(0, query.Skip)).Take(Math.Clamp(query.Take, 1, 200)).Select(ToPreviewRow).ToList();
        return new DatasetExplorerResultDto { TotalMatches = list.Count, Rows = page };
    }

    private static Dictionary<string, int> ComputeDiseaseDistribution(IReadOnlyList<BraxCsvRow> rows)
    {
        var dist = BraxLabelColumns.ToDictionary(c => c, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var label in row.ActiveLabels)
            {
                if (dist.ContainsKey(label)) dist[label]++;
            }
        }
        return dist;
    }

    private static BraxCsvRow MapRow(string[] parts, Dictionary<string, int> colMap, int lineNo)
    {
        string Get(string key) => colMap.TryGetValue(key, out var idx) && idx < parts.Length ? parts[idx].Trim() : "";
        var patientId = Get("PatientID");
        if (string.IsNullOrEmpty(patientId)) patientId = Get("PatientId");
        var studyId = Get("AccessionNumber");
        if (string.IsNullOrEmpty(studyId)) studyId = Get("StudyDate");
        var dicom = Get("DicomPath");
        if (string.IsNullOrEmpty(dicom)) dicom = Get("DICOMPath");
        var png = Get("PngPath");
        if (string.IsNullOrEmpty(png)) png = Get("PNGPath");
        if (string.IsNullOrEmpty(png)) png = Get("ImagePath");
        var view = Get("ViewPosition");
        var imageName = !string.IsNullOrEmpty(png) ? Path.GetFileName(png) : !string.IsNullOrEmpty(dicom) ? Path.GetFileName(dicom) : $"row_{lineNo}";

        var activeLabels = new List<string>();
        foreach (var label in BraxLabelColumns)
        {
            if (!colMap.TryGetValue(NormalizeHeader(label), out var idx) || idx >= parts.Length) continue;
            if (IsPositive(parts[idx])) activeLabels.Add(label);
        }

        var isNormal = activeLabels.Count == 0 ||
                       (activeLabels.Count == 1 && activeLabels[0].Equals("No Finding", StringComparison.OrdinalIgnoreCase));

        return new BraxCsvRow
        {
            PatientId = patientId,
            StudyId = studyId,
            ImageName = imageName,
            ViewPosition = view,
            DicomPath = dicom,
            PngPath = png,
            ActiveLabels = activeLabels,
            LabelsText = activeLabels.Count == 0 ? "Normal" : string.Join(", ", activeLabels),
            IsNormal = isNormal,
        };
    }

    private static DatasetPreviewRowDto ToPreviewRow(BraxCsvRow r) => new()
    {
        PatientId = r.PatientId,
        StudyId = r.StudyId,
        ImageName = r.ImageName,
        Labels = r.LabelsText,
        ViewPosition = r.ViewPosition,
        DicomPath = r.DicomPath,
        PngPath = r.PngPath,
    };

    private static Dictionary<string, int> BuildColumnMap(string[] headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
            map[NormalizeHeader(headers[i])] = i;
        return map;
    }

    private static string NormalizeHeader(string h) => h.Trim().Replace(" ", "", StringComparison.Ordinal);

    private static bool IsPositive(string value) =>
        value.Trim() is "1" or "1.0" or "True" or "true" or "YES" or "yes";

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string InferVersionLabel(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        return name.StartsWith("BRAX", StringComparison.OrdinalIgnoreCase) ? name : $"Dataset_{name}";
    }

    private static string ComputePathHash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path)))).ToLowerInvariant();
    }

    private static long DirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
    }

    private sealed class BraxCsvRow
    {
        public string PatientId { get; init; } = "";
        public string StudyId { get; init; } = "";
        public string ImageName { get; init; } = "";
        public string ViewPosition { get; init; } = "";
        public string DicomPath { get; init; } = "";
        public string PngPath { get; init; } = "";
        public string LabelsText { get; init; } = "";
        public List<string> ActiveLabels { get; init; } = [];
        public bool IsNormal { get; init; }
    }
}

public sealed class BraxPluginMetadataProvider : IModelPluginMetadataProvider
{
    public string ModelId => ModelTrainingNames.CheXNet;
    private readonly BraxDatasetCsvService _csv;

    public BraxPluginMetadataProvider(BraxDatasetCsvService csv) => _csv = csv;

    public ModelArchitectureDto GetArchitecture() => new()
    {
        ModelId = ModelId,
        ModelName = "CheXNet (BRAX)",
        Backbone = "DenseNet121",
        Classifier = "Linear (14 classes)",
        InputSize = "224 × 224",
        OutputClasses = 14,
        TrainableParameters = 2_000_000,
        FrozenParameters = 6_000_000,
        TotalParameters = 8_000_000,
        TrainingStrategy = "Transfer learning with optional last-block unfreeze",
        TransferLearningStrategy = "ImageNet → CheXNet → BRAX fine-tune",
        DiagramLayers =
        [
            "DenseNet121",
            "↓",
            "Frozen Backbone",
            "↓",
            "DenseBlock4",
            "↓",
            "Classifier",
            "↓",
            "Sigmoid",
        ],
    };

    public Task<DatasetPreviewDto?> BuildDatasetPreviewAsync(string datasetPath, CancellationToken ct = default) =>
        Task.FromResult<DatasetPreviewDto?>(_csv.BuildPreview(datasetPath));

    public Task<DatasetStatisticsDto?> BuildDatasetStatisticsAsync(string datasetPath, CancellationToken ct = default) =>
        Task.FromResult<DatasetStatisticsDto?>(_csv.BuildStatistics(datasetPath));
}

public sealed class HitlPluginMetadataProvider : IModelPluginMetadataProvider
{
    private readonly string _modelId;
    private readonly string _displayName;

    public HitlPluginMetadataProvider(string modelId, string displayName)
    {
        _modelId = modelId;
        _displayName = displayName;
    }

    public string ModelId => _modelId;

    public ModelArchitectureDto GetArchitecture() => new()
    {
        ModelId = _modelId,
        ModelName = _displayName,
        Backbone = "Model-specific",
        Classifier = "Task head",
        InputSize = "Model-specific",
        OutputClasses = 1,
        TrainingStrategy = "Human-in-the-loop retraining from doctor feedback",
        TransferLearningStrategy = "Incremental fine-tune on accepted/modified cases",
        DiagramLayers = [_displayName, "↓", "Feedback Loop", "↓", "Retrain", "↓", "Deploy Candidate"],
    };

    public Task<DatasetPreviewDto?> BuildDatasetPreviewAsync(string datasetPath, CancellationToken ct = default) =>
        Task.FromResult<DatasetPreviewDto?>(null);

    public Task<DatasetStatisticsDto?> BuildDatasetStatisticsAsync(string datasetPath, CancellationToken ct = default) =>
        Task.FromResult<DatasetStatisticsDto?>(null);
}

public sealed class ModelPluginMetadataRegistry
{
    private readonly IReadOnlyDictionary<string, IModelPluginMetadataProvider> _providers;
    private readonly ModelTrainingPipelineRegistry _pipelines;

    public ModelPluginMetadataRegistry(
        IEnumerable<IModelPluginMetadataProvider> providers,
        ModelTrainingPipelineRegistry pipelines)
    {
        _providers = providers.ToDictionary(p => p.ModelId, StringComparer.OrdinalIgnoreCase);
        _pipelines = pipelines;
    }

    public IModelPluginMetadataProvider? Get(string modelId) =>
        _providers.GetValueOrDefault(modelId);

    public ModelArchitectureDto? GetArchitecture(string modelId) =>
        Get(modelId)?.GetArchitecture();

    public List<ModelPluginInfoDto> ListRegisteredPlugins()
    {
        return _pipelines.All.Select(p =>
        {
            var meta = Get(p.ModelId);
            return new ModelPluginInfoDto
            {
                ModelId = p.ModelId,
                DisplayName = p.DisplayName,
                PipelineKind = p.PipelineKind,
                HasTrainingPipeline = true,
                HasDatasetHandler = meta is BraxPluginMetadataProvider,
                HasArchitecture = meta is not null,
            };
        }).ToList();
    }
}

public sealed class TrainingCenterExtensionService
{
    private readonly ApplicationDbContext _db;
    private readonly CheXNetPathResolver _paths;
    private readonly BraxDatasetCsvService _csv;
    private readonly ModelPluginMetadataRegistry _plugins;
    private readonly TrainingMonitorReader _monitor;
    private readonly TrainingJobRuntimeStore _runtime;
    private readonly TrainingRecommendationService _recommendations;
    private readonly TrainingResourceMonitorService _resources;
    private readonly DatasetArchiveService _datasetArchive;
    private readonly DeploymentHistoryService _deploymentHistory;
    private readonly ModelTrainingPipelineRegistry _pipelines;

    public TrainingCenterExtensionService(
        ApplicationDbContext db,
        CheXNetPathResolver paths,
        BraxDatasetCsvService csv,
        ModelPluginMetadataRegistry plugins,
        TrainingMonitorReader monitor,
        TrainingJobRuntimeStore runtime,
        TrainingRecommendationService recommendations,
        TrainingResourceMonitorService resources,
        DatasetArchiveService datasetArchive,
        DeploymentHistoryService deploymentHistory,
        ModelTrainingPipelineRegistry pipelines)
    {
        _db = db;
        _paths = paths;
        _csv = csv;
        _plugins = plugins;
        _monitor = monitor;
        _runtime = runtime;
        _recommendations = recommendations;
        _resources = resources;
        _datasetArchive = datasetArchive;
        _deploymentHistory = deploymentHistory;
        _pipelines = pipelines;
    }

    public async Task<DashboardHomeSummaryDto> BuildHomeSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            var models = _pipelines.All.Count;
            var datasets = (await _datasetArchive.ListAsync(ct).ConfigureAwait(false)).Count;
            var running = await _db.TrainingJobs.CountAsync(
                j => j.Status == TrainingJobStatuses.Running || j.Status == TrainingJobStatuses.Preparing, ct).ConfigureAwait(false);
            var queued = await _db.TrainingJobs.CountAsync(j => j.Status == TrainingJobStatusesExtended.Queued, ct).ConfigureAwait(false);
            var finished = await _db.TrainingJobs.CountAsync(
                j => j.Status == TrainingJobStatuses.Completed || j.Status == TrainingJobStatuses.Failed, ct).ConfigureAwait(false);
            var deployable = await _db.ModelVersions.CountAsync(v => v.IsDeployable && !v.IsProduction, ct).ConfigureAwait(false);
            var production = await _db.ModelVersions.CountAsync(v => v.IsProduction, ct).ConfigureAwait(false);
            var latestTraining = await _db.TrainingJobs.AsNoTracking()
                .Where(j => j.Status == TrainingJobStatuses.Completed)
                .OrderByDescending(j => j.FinishedAt)
                .Select(j => j.ModelName + " · " + (j.NewModelVersion ?? j.FinishedAt!.Value.ToString("u")))
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            var latestDeploy = await _db.DeploymentHistoryRecords.AsNoTracking()
                .OrderByDescending(d => d.DeployedAt)
                .Select(d => d.ModelName + " → " + d.ToVersion)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

            double storageGb = 0;
            if (Directory.Exists(_paths.ResolveBraxRoot())) storageGb += DirSizeGb(_paths.ResolveBraxRoot());
            if (Directory.Exists(_paths.ResolveModelVersionsDirectory())) storageGb += DirSizeGb(_paths.ResolveModelVersionsDirectory());
            if (Directory.Exists(_paths.ResolveDatasetUploadDirectory())) storageGb += DirSizeGb(_paths.ResolveDatasetUploadDirectory());

            return new DashboardHomeSummaryDto
            {
                ModelCount = models,
                DatasetCount = datasets,
                RunningJobs = running,
                QueuedJobs = queued,
                FinishedJobs = finished,
                DeployableModels = deployable,
                ProductionModels = production,
                StorageUsedGb = Math.Round(storageGb, 2),
                LatestTraining = latestTraining,
                LatestDeployment = latestDeploy,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new DashboardHomeSummaryDto { ModelCount = _pipelines.All.Count };
        }
    }

    public TrainingWizardStateDto BuildWizardState(
        string? modelId,
        string? datasetPath,
        bool datasetValidated,
        bool trainingStarted,
        bool trainingCompleted,
        bool deployed)
    {
        string Status(bool done, bool current) => done ? "completed" : current ? "current" : "pending";
        var hasModel = !string.IsNullOrWhiteSpace(modelId);
        var hasDataset = !string.IsNullOrWhiteSpace(datasetPath);
        var monitoring = trainingStarted && !trainingCompleted;
        var compareReady = trainingCompleted;

        return new TrainingWizardStateDto
        {
            CurrentStep = deployed ? 9
                : compareReady ? 8
                : monitoring ? 7
                : trainingStarted ? 6
                : hasDataset && datasetValidated ? 5
                : datasetValidated ? 4
                : hasDataset ? 3
                : hasModel ? 2
                : 1,
            Steps =
            [
                new() { Step = 1, Title = "Choose Model", Status = Status(hasModel, !hasModel) },
                new() { Step = 2, Title = "Choose Dataset", Status = Status(hasDataset, hasModel && !hasDataset) },
                new() { Step = 3, Title = "Validate Dataset", Status = Status(datasetValidated, hasDataset && !datasetValidated) },
                new() { Step = 4, Title = "Configure Fine-Tuning", Status = Status(trainingStarted || compareReady, datasetValidated && !trainingStarted) },
                new() { Step = 5, Title = "Review Configuration", Status = Status(trainingStarted || compareReady, datasetValidated && !trainingStarted) },
                new() { Step = 6, Title = "Start Training", Status = Status(trainingStarted || compareReady, datasetValidated && !trainingStarted) },
                new() { Step = 7, Title = "Monitor Training", Status = Status(trainingCompleted, monitoring) },
                new() { Step = 8, Title = "Compare Results", Status = Status(deployed, compareReady && !deployed) },
                new() { Step = 9, Title = "Deploy", Status = Status(deployed, compareReady && !deployed) },
            ],
        };
    }

    public WorkflowTimelineDto BuildWorkflow(string? datasetPath, string? jobStatus, bool deployed)
    {
        string Stage(string id, string label, bool done, bool current) =>
            done ? "completed" : current ? "current" : "pending";

        var hasDataset = !string.IsNullOrWhiteSpace(datasetPath);
        var validated = hasDataset;
        var subset = jobStatus is TrainingJobStatuses.Preparing or TrainingJobStatuses.Running
            or TrainingJobStatuses.Completed;
        var training = jobStatus is TrainingJobStatuses.Running or TrainingJobStatuses.Completed;
        var evaluation = jobStatus == TrainingJobStatuses.Completed;
        var comparison = evaluation;
        var deployment = deployed;

        var current = deployment ? "deployment"
            : comparison ? "comparison"
            : evaluation ? "evaluation"
            : training ? "training"
            : subset ? "subset"
            : validated ? "validation"
            : hasDataset ? "dataset" : "dataset";

        return new WorkflowTimelineDto
        {
            CurrentStageId = current,
            Stages =
            [
                new() { Id = "dataset", Label = "Dataset Selected", Status = Stage("dataset", "Dataset Selected", hasDataset, !hasDataset) },
                new() { Id = "validation", Label = "Validation", Status = Stage("validation", "Validation", validated, hasDataset && !validated) },
                new() { Id = "subset", Label = "Subset Creation", Status = Stage("subset", "Subset Creation", subset, validated && !subset) },
                new() { Id = "training", Label = "Training", Status = Stage("training", "Training", training, subset && !training) },
                new() { Id = "evaluation", Label = "Evaluation", Status = Stage("evaluation", "Evaluation", evaluation, training && !evaluation) },
                new() { Id = "comparison", Label = "Comparison", Status = Stage("comparison", "Comparison", comparison, evaluation && !comparison) },
                new() { Id = "deployment", Label = "Deployment", Status = Stage("deployment", "Deployment", deployment, comparison && !deployment) },
            ],
        };
    }

    public async Task<List<DatasetVersionRowDto>> ListDatasetVersionsAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await _db.DatasetArchiveEntries.AsNoTracking()
                .Where(d => !d.IsDeleted)
                .OrderByDescending(d => d.UploadDate)
                .ToListAsync(ct).ConfigureAwait(false);
            return rows.Select(d => new DatasetVersionRowDto
            {
                Version = d.VersionLabel ?? d.Name,
                Path = d.Path,
                ContentHash = d.ContentHash ?? d.PathHash,
                CreatedDate = d.UploadDate,
                ValidationStatus = d.ValidationStatus ?? "Unknown",
            }).ToList();
        }
        catch
        {
            var brax = _paths.ResolveBraxRoot();
            if (!Directory.Exists(brax)) return [];
            return
            [
                new DatasetVersionRowDto
                {
                    Version = "BRAX_v1",
                    Path = brax,
                    ContentHash = _csv.ComputeContentHash(brax),
                    CreatedDate = Directory.GetCreationTimeUtc(brax),
                    ValidationStatus = "Legacy",
                },
            ];
        }
    }

    public TrainingConsoleDto ReadConsole(Guid? jobId)
    {
        BraxTrainingJobRuntime? job = jobId.HasValue ? _runtime.Get(jobId.Value) : _runtime.GetActive();
        if (job is null) return new TrainingConsoleDto { IsRunning = false };

        var logPath = job.LogPath;
        if (!string.IsNullOrEmpty(job.OutputDir))
        {
            var alt = Path.Combine(job.OutputDir, "training.log");
            if (File.Exists(alt)) logPath = alt;
        }

        var text = !string.IsNullOrEmpty(logPath) && File.Exists(logPath)
            ? File.ReadAllText(logPath)
            : "";

        return new TrainingConsoleDto
        {
            JobId = job.JobId,
            LogText = text,
            IsRunning = job.Status is TrainingJobStatuses.Running or TrainingJobStatuses.Preparing,
        };
    }

    public async Task<CheckpointDetailsDto?> GetCheckpointDetailsAsync(
        string checkpointId,
        IReadOnlyList<CheckpointRowDto> allCheckpoints,
        CancellationToken ct = default)
    {
        var cp = allCheckpoints.FirstOrDefault(c => c.Id == checkpointId);
        if (cp is null) return null;

        var dir = Directory.Exists(cp.FilePath) ? cp.FilePath : Path.GetDirectoryName(cp.FilePath) ?? cp.FilePath;
        if (!Directory.Exists(dir))
        {
            var versionsDir = _paths.ResolveModelVersionsDirectory();
            var candidate = Path.Combine(versionsDir, checkpointId);
            if (Directory.Exists(candidate)) dir = candidate;
        }

        var charts = Directory.Exists(dir) ? _monitor.ReadCharts(dir) : new TrainingChartsDto();
        var architecture = _plugins.GetArchitecture(cp.ModelId);
        var dbVersion = await _db.ModelVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id.ToString() == checkpointId || v.VersionNumber == checkpointId, ct)
            .ConfigureAwait(false);

        var links = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddDownloadLink(links, dir, "metrics.json");
        AddDownloadLink(links, dir, "loss.csv");
        AddDownloadLink(links, dir, "training.log");
        AddDownloadLink(links, dir, "training_summary.txt");

        var recommendation = dbVersion?.TrainingJobId is Guid jid
            ? _recommendations.BuildRecommendation(jid, allCheckpoints)
            : null;

        return new CheckpointDetailsDto
        {
            Checkpoint = cp,
            Optimizer = "Adam",
            LearningRate = dbVersion is not null ? await ReadJobLearningRateAsync(dbVersion.TrainingJobId, ct) : null,
            Epochs = cp.Epochs,
            Dataset = cp.Dataset,
            DatasetVersion = dbVersion?.DatasetVersion ?? cp.DatasetVersion,
            DatasetHash = dbVersion?.DatasetHash ?? cp.DatasetHash,
            ArchitectureSummary = architecture?.Backbone,
            TrainableParameters = architecture?.TrainableParameters,
            FrozenParameters = architecture?.FrozenParameters,
            Charts = charts,
            Architecture = architecture,
            DownloadLinks = links,
            Notes = dbVersion?.Notes ?? cp.Notes,
            Flags = new CheckpointFlagsDto
            {
                IsFavorite = dbVersion?.IsFavorite ?? cp.IsFavorite,
                IsPinned = dbVersion?.IsPinned ?? cp.IsPinned,
                IsProductionCandidate = dbVersion?.IsProductionCandidate ?? cp.IsProductionCandidate,
                IsRecommended = dbVersion?.IsRecommended ?? cp.IsRecommended,
            },
            Recommendation = recommendation,
        };
    }

    public EnhancedRecommendationDto BuildEnhancedRecommendation(
        Guid jobId,
        IReadOnlyList<CheckpointRowDto> checkpoints)
    {
        var baseRec = _recommendations.BuildRecommendation(jobId, checkpoints);
        var insights = new List<string>();
        if (baseRec?.ValLossImprovement(out var vlMsg) == true) insights.Add(vlMsg);
        if (baseRec?.RocAucDelta is > 0.01) insights.Add("ROC-AUC increased.");
        else if (baseRec?.RocAucDelta is < -0.01) insights.Add("ROC-AUC decreased.");
        if (baseRec?.F1Delta is > 0.01) insights.Add("F1 improved.");
        else if (baseRec?.F1Delta is < -0.01) insights.Add("F1 decreased.");
        if (baseRec?.Candidate?.ValLoss is { } cv && baseRec.CurrentProduction?.ValLoss is { } pv && cv > pv * 1.15)
            insights.Add("Overfitting detected (validation loss diverging).");

        var action = baseRec?.Recommendation ?? "Review metrics before deployment.";
        if (insights.Count == 0) insights.Add("Metrics are within expected range.");

        return new EnhancedRecommendationDto
        {
            Base = baseRec,
            Insights = insights,
            RecommendedAction = $"Recommended action: {action}.",
        };
    }

    private async Task<double?> ReadJobLearningRateAsync(Guid? jobId, CancellationToken ct)
    {
        if (jobId is null) return null;
        var job = await _db.TrainingJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct).ConfigureAwait(false);
        return job?.LearningRate;
    }

    private static void AddDownloadLink(Dictionary<string, string> links, string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path)) links[fileName] = path;
    }

    private static double DirSizeGb(string path)
    {
        long bytes = 0;
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            bytes += f.Length;
        return bytes / (1024.0 * 1024 * 1024);
    }
}

internal static class TrainingRecommendationExtensions
{
    public static bool ValLossImprovement(this TrainingRecommendationDto? rec, out string message)
    {
        message = "";
        if (rec?.Candidate?.ValLoss is null) return false;
        if (rec.CurrentProduction?.ValLoss is { } prod && rec.Candidate.ValLoss < prod)
        {
            message = "Validation loss improved.";
            return true;
        }
        if (rec.CurrentProduction is null)
        {
            message = "First deployable checkpoint recorded.";
            return true;
        }
        return false;
    }
}

public sealed class TrainingThresholdAnalyzerService
{
    private readonly PythonScriptRunner _python;
    private readonly CheXNetPathResolver _paths;

    public TrainingThresholdAnalyzerService(PythonScriptRunner python, CheXNetPathResolver paths)
    {
        _python = python;
        _paths = paths;
    }

    public async Task<ThresholdAnalysisDto> AnalyzeAsync(string checkpointId, double threshold, CancellationToken ct = default)
    {
        threshold = Math.Clamp(threshold, 0.01, 0.90);
        var versionsDir = _paths.ResolveModelVersionsDirectory();
        var dir = Path.Combine(versionsDir, checkpointId);
        if (!Directory.Exists(dir))
            throw new InvalidOperationException("Checkpoint directory not found.");

        var metricsPath = Path.Combine(dir, "metrics.json");
        var checkpointPath = Path.Combine(dir, "model.pth.tar");
        if (!File.Exists(metricsPath))
            throw new InvalidOperationException("metrics.json not found for checkpoint.");

        if (!File.Exists(checkpointPath) && File.Exists(Path.Combine(dir, "best_model.pth.tar")))
            checkpointPath = Path.Combine(dir, "best_model.pth.tar");

        var outputPath = Path.Combine(_paths.ResolveJobStateDirectory(), $"threshold_{Guid.NewGuid():N}.json");
        var args = new List<string>
        {
            "--checkpoint", checkpointPath,
            "--metrics", metricsPath,
            "--threshold", threshold.ToString(CultureInfo.InvariantCulture),
            "--output", outputPath,
        };

        var (code, log) = await _python.RunAsync("admin_threshold_analyzer.py", args, ct).ConfigureAwait(false);
        if (code != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"Threshold analysis failed: {log}");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, ct).ConfigureAwait(false));
        var root = doc.RootElement;
        var curve = new List<ChartPointDto>();
        if (root.TryGetProperty("f1_curve", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var pt in arr.EnumerateArray())
                curve.Add(new ChartPointDto
                {
                    Epoch = pt.TryGetProperty("epoch", out var ep) ? ep.GetInt32() : 0,
                    Value = pt.TryGetProperty("value", out var v) ? v.GetDouble() : 0,
                });
        }

        return new ThresholdAnalysisDto
        {
            Threshold = threshold,
            Accuracy = ReadD(root, "accuracy"),
            Precision = ReadD(root, "precision_micro"),
            Recall = ReadD(root, "recall_micro"),
            F1 = ReadD(root, "f1_micro"),
            Specificity = ReadD(root, "specificity"),
            Sensitivity = ReadD(root, "sensitivity"),
            PredictedPositives = ReadI(root, "predicted_positives"),
            FalsePositives = ReadI(root, "false_positives"),
            FalseNegatives = ReadI(root, "false_negatives"),
            F1Curve = curve,
        };
    }

    private static double? ReadD(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? ReadI(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}

public sealed class CheckpointMetadataService
{
    private readonly ApplicationDbContext _db;
    private readonly CheXNetPathResolver _paths;

    public CheckpointMetadataService(ApplicationDbContext db, CheXNetPathResolver paths)
    {
        _db = db;
        _paths = paths;
    }

    public async Task<(bool ok, string message)> UpdateFlagsAsync(CheckpointFlagsUpdateDto dto, CancellationToken ct = default)
    {
        var version = await FindVersionAsync(dto.CheckpointId, ct).ConfigureAwait(false);
        if (version is null)
        {
            await WriteSidecarFlagsAsync(dto).ConfigureAwait(false);
            return (true, "Flags saved to checkpoint metadata.");
        }

        if (dto.IsFavorite is { } fav) version.IsFavorite = fav;
        if (dto.IsPinned is { } pin) version.IsPinned = pin;
        if (dto.IsProductionCandidate is { } pc) version.IsProductionCandidate = pc;
        if (dto.IsRecommended is { } rec) version.IsRecommended = rec;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Checkpoint flags updated.");
    }

    public async Task<(bool ok, string message)> UpdateNotesAsync(CheckpointNotesUpdateDto dto, CancellationToken ct = default)
    {
        var version = await FindVersionAsync(dto.CheckpointId, ct).ConfigureAwait(false);
        if (version is null)
        {
            var dir = ResolveCheckpointDir(dto.CheckpointId);
            if (dir is null) return (false, "Checkpoint not found.");
            var sidecar = Path.Combine(dir, "checkpoint_meta.json");
            var doc = File.Exists(sidecar)
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(sidecar)) ?? new()
                : new Dictionary<string, JsonElement>();
            doc["notes"] = JsonSerializer.SerializeToElement(dto.Notes);
            await File.WriteAllTextAsync(sidecar, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }), ct)
                .ConfigureAwait(false);
            return (true, "Notes saved.");
        }

        version.Notes = dto.Notes;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Notes saved.");
    }

    private async Task<ModelVersion?> FindVersionAsync(string checkpointId, CancellationToken ct) =>
        await _db.ModelVersions.FirstOrDefaultAsync(
            v => v.Id.ToString() == checkpointId || v.VersionNumber == checkpointId, ct).ConfigureAwait(false);

    private string? ResolveCheckpointDir(string checkpointId)
    {
        var dir = Path.Combine(_paths.ResolveModelVersionsDirectory(), checkpointId);
        return Directory.Exists(dir) ? dir : null;
    }

    private async Task WriteSidecarFlagsAsync(CheckpointFlagsUpdateDto dto)
    {
        var dir = ResolveCheckpointDir(dto.CheckpointId);
        if (dir is null) return;
        var sidecar = Path.Combine(dir, "checkpoint_meta.json");
        var existing = File.Exists(sidecar)
            ? JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(sidecar)) ?? new()
            : new Dictionary<string, bool>();
        if (dto.IsFavorite is { } f) existing["isFavorite"] = f;
        if (dto.IsPinned is { } p) existing["isPinned"] = p;
        if (dto.IsProductionCandidate is { } pc) existing["isProductionCandidate"] = pc;
        if (dto.IsRecommended is { } r) existing["isRecommended"] = r;
        await File.WriteAllTextAsync(sidecar, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class ProductionIntegrationTestService
{
    private readonly CheXNetApiClient _chexNet;
    private readonly ApplicationDbContext _db;

    public ProductionIntegrationTestService(CheXNetApiClient chexNet, ApplicationDbContext db)
    {
        _chexNet = chexNet;
        _db = db;
    }

    public async Task<ProductionIntegrationTestResultDto> RunAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var productionVersion = await _db.ModelVersions.AsNoTracking()
                .Where(v => v.ModelName == ModelTrainingNames.CheXNet && v.IsProduction)
                .Select(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "production-default";

            var result = await _chexNet.PredictAsync(imageBytes, fileName, contentType, cancellationToken: ct).ConfigureAwait(false);
            sw.Stop();
            var parsed = result.GetValueOrDefault("CheXNet");
            var top = parsed?.TopK.FirstOrDefault();
            return new ProductionIntegrationTestResultDto
            {
                ModelVersion = productionVersion,
                TopPrediction = top?.ClassName ?? "—",
                Confidence = top?.Probability ?? 0,
                InferenceTimeMs = parsed?.InferenceMs ?? sw.Elapsed.TotalMilliseconds,
                Predictions = (parsed?.TopK ?? []).Select(t => new InferenceLabelScoreDto
                {
                    Label = t.ClassName,
                    Confidence = t.Probability,
                }).ToList(),
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProductionIntegrationTestResultDto { Error = ex.Message, InferenceTimeMs = sw.Elapsed.TotalMilliseconds };
        }
    }
}

public sealed class DatasetVersioningService
{
    private readonly ApplicationDbContext _db;
    private readonly BraxDatasetCsvService _csv;

    public DatasetVersioningService(ApplicationDbContext db, BraxDatasetCsvService csv)
    {
        _db = db;
        _csv = csv;
    }

    public async Task<(string versionLabel, string contentHash)> AssignVersionAsync(
        string datasetPath,
        bool validationPassed,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(datasetPath);
        var contentHash = _csv.ComputeContentHash(fullPath);
        try
        {
            var existing = await _db.DatasetArchiveEntries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.ContentHash == contentHash && !d.IsDeleted, ct).ConfigureAwait(false);
            if (existing?.VersionLabel is not null)
                return (existing.VersionLabel, contentHash);

            var prefix = fullPath.Contains("BRAX", StringComparison.OrdinalIgnoreCase) ? "BRAX" : "Dataset";
            var count = await _db.DatasetArchiveEntries.CountAsync(
                d => d.VersionLabel != null && d.VersionLabel.StartsWith(prefix + "_v"), ct).ConfigureAwait(false);
            var label = $"{prefix}_v{count + 1}";
            var entry = await _db.DatasetArchiveEntries
                .FirstOrDefaultAsync(d => d.PathHash == ComputePathHash(fullPath) && !d.IsDeleted, ct).ConfigureAwait(false);
            if (entry is not null)
            {
                if (string.IsNullOrEmpty(entry.VersionLabel))
                {
                    entry.VersionLabel = label;
                    entry.ContentHash = contentHash;
                    entry.ValidationStatus = validationPassed ? "Validated" : "Failed";
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                return (entry.VersionLabel ?? label, contentHash);
            }
            return (label, contentHash);
        }
        catch
        {
            return ("BRAX_v1", contentHash);
        }
    }

    private static string ComputePathHash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
    }
}
