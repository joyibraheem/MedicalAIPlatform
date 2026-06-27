namespace MedicalAIPlatform.Models.TrainingCenter;

public sealed class TrainingCenterDashboardDto
{
    public List<ModelManagementCardDto> Models { get; init; } = [];
    public List<StoredDatasetOptionDto> StoredDatasets { get; init; } = [];
    public List<CheckpointRowDto> Checkpoints { get; init; } = [];
    public List<TrainingHistoryRowDto> History { get; init; } = [];
    public SystemStatusDto System { get; init; } = new();
    public LiveTrainingMonitorDto? ActiveJob { get; init; }
    public int ModifiedThreshold { get; init; }
    public List<TrainingQueueItemDto> Queue { get; init; } = [];
    public List<TrainingNotificationDto> Notifications { get; init; } = [];
    public int UnreadNotificationCount { get; init; }
    public List<DatasetArchiveRowDto> DatasetArchive { get; init; } = [];
    public List<HyperparameterPresetDto> Presets { get; init; } = [];
    public List<ExperimentRowDto> Experiments { get; init; } = [];
    public List<DeploymentHistoryRowDto> DeploymentHistory { get; init; } = [];
    public List<ModelVersionTimelineRowDto> VersionTimeline { get; init; } = [];
    public TrainingRecommendationDto? LatestRecommendation { get; init; }
    public LiveResourceMonitorDto? LiveResources { get; init; }
    public DashboardHomeSummaryDto? HomeSummary { get; init; }
    public WorkflowTimelineDto? Workflow { get; init; }
    public List<ModelPluginInfoDto> RegisteredPlugins { get; init; } = [];
}

public sealed class ModelPluginInfoDto
{
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string PipelineKind { get; init; } = "";
    public bool HasTrainingPipeline { get; init; }
    public bool HasDatasetHandler { get; init; }
    public bool HasArchitecture { get; init; }
}

public sealed class ModelManagementCardDto
{
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string PipelineKind { get; init; } = "";
    public string CurrentVersion { get; init; } = "1.0";
    public string Status { get; init; } = "Idle";
    public DateTimeOffset? LastTrainingDate { get; init; }
    public string? ProductionCheckpoint { get; init; }
    public string? DatasetName { get; init; }
    public string DeployStatus { get; init; } = "Production stable";
    public double? ProductionAccuracy { get; init; }
    public double? ProductionF1 { get; init; }
    public double? ProductionRocAuc { get; init; }
    public int DeployableVersionsCount { get; init; }
    public int HitlAcceptedCount { get; init; }
    public int HitlModifiedCount { get; init; }
    public bool HitlRetrainingReady { get; init; }
    public TrainingConfigDefaultsDto ConfigDefaults { get; init; } = new();
}

public sealed class TrainingConfigDefaultsDto
{
    public int SubsetSize { get; init; } = 100;
    public int[] AllowedSubsetSizes { get; init; } = [100, 300, 600, 1000, 3000];
    public int Epochs { get; init; } = 2;
    public double LearningRate { get; init; } = 1e-4;
    public int BatchSize { get; init; } = 8;
    public double ValRatio { get; init; } = 0.2;
    public int EarlyStoppingPatience { get; init; } = 0;
    public bool FreezeBackbone { get; init; } = true;
    public bool UnfreezeLastBlock { get; init; }
    public int NumWorkers { get; init; }
    public int RandomSeed { get; init; } = 42;
    public bool SaveBestModel { get; init; } = true;
}

public sealed class StoredDatasetOptionDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Kind { get; init; } = "";
}

public sealed class DatasetValidationResultDto
{
    public bool Ready { get; init; }
    public string DatasetName { get; init; } = "";
    public string DatasetPath { get; init; } = "";
    public long? DatasetSizeBytes { get; init; }
    public int TotalRows { get; init; }
    public int ValidRows { get; init; }
    public int MissingImages { get; init; }
    public int EstimatedPatients { get; init; }
    public int PositiveImages { get; init; }
    public int NormalImages { get; init; }
    public double ValRatio { get; init; }
    public Dictionary<string, int> DiseaseDistribution { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public string? ReportJsonPath { get; init; }
}

public sealed class TrainingStartRequestDto
{
    public string ModelId { get; set; } = "";
    public string DatasetPath { get; set; } = "";
    public string? DatasetName { get; set; }
    public int SubsetSize { get; set; } = 100;
    public int Epochs { get; set; } = 2;
    public double LearningRate { get; set; } = 1e-4;
    public int BatchSize { get; set; } = 8;
    public double ValRatio { get; set; } = 0.2;
    public int? EarlyStoppingPatience { get; set; }
    public bool UnfreezeLastBlock { get; set; }
    public bool FreezeBackbone { get; set; } = true;
    public string? ResumeCheckpoint { get; set; }
    public string? FinetuneCheckpoint { get; set; }
    public int NumWorkers { get; set; } = 0;
    public int RandomSeed { get; set; } = 42;
    public bool PrepareSubset { get; set; } = true;
}

public sealed class TrainingStartResponseDto
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public Guid? JobId { get; init; }
    public string? OutputDir { get; init; }
    public TrainingConfirmationDto? Confirmation { get; init; }
}

public sealed class TrainingConfirmationDto
{
    public string Model { get; init; } = "";
    public string Dataset { get; init; } = "";
    public int SubsetSize { get; init; }
    public int Epochs { get; init; }
    public double LearningRate { get; init; }
    public string CheckpointSource { get; init; } = "";
    public string EstimatedDuration { get; init; } = "";
    public string EstimatedRam { get; init; } = "";
}

public sealed class LiveTrainingMonitorDto
{
    public Guid JobId { get; init; }
    public string ModelId { get; init; } = "";
    public string Status { get; init; } = "";
    public int? CurrentEpoch { get; set; }
    public int? TotalEpochs { get; set; }
    public string? CurrentBatch { get; set; }
    public double? TrainLoss { get; set; }
    public double? ValLoss { get; set; }
    public double? Accuracy { get; set; }
    public double? Precision { get; set; }
    public double? Recall { get; set; }
    public double? F1 { get; set; }
    public double? RocAuc { get; set; }
    public double? LearningRate { get; set; }
    public double ElapsedSeconds { get; init; }
    public double? EtaSeconds { get; set; }
    public double ProgressPercent { get; set; }
    public string? OutputDir { get; set; }
    public string? LogTail { get; set; }
    public bool CanCancel { get; init; }
    public bool CanPause { get; init; }
    public bool CanResume { get; init; }
}

public sealed class TrainingChartsDto
{
    public List<ChartPointDto> TrainLoss { get; init; } = [];
    public List<ChartPointDto> ValLoss { get; init; } = [];
    public List<ChartPointDto> F1 { get; init; } = [];
    public List<ChartPointDto> RocAuc { get; init; } = [];
    public List<ChartPointDto> LearningRate { get; init; } = [];
}

public sealed class ChartPointDto
{
    public int Epoch { get; init; }
    public double Value { get; init; }
}

public sealed class CheckpointRowDto
{
    public string Id { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTimeOffset TrainingDate { get; init; }
    public string Dataset { get; init; } = "";
    public int? SubsetSize { get; init; }
    public int? Epochs { get; init; }
    public double? Accuracy { get; init; }
    public double? F1 { get; init; }
    public double? RocAuc { get; init; }
    public double? ValLoss { get; init; }
    public double? TrainingTimeSeconds { get; init; }
    public string Status { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool IsProduction { get; init; }
    public bool IsDeployable { get; init; }
    public Guid? TrainingJobId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
    public bool IsProductionCandidate { get; init; }
    public bool IsRecommended { get; init; }
    public string? DatasetVersion { get; init; }
    public string? DatasetHash { get; init; }
    public string? ValidationStatus { get; init; }
    public string? Notes { get; init; }
    public string? ReportPath { get; init; }
}

public sealed class CheckpointCompareRequestDto
{
    public List<string> CheckpointIds { get; set; } = [];
}

public sealed class CheckpointCompareRowDto
{
    public string Metric { get; init; } = "";
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? BestCheckpointId { get; init; }
}

public sealed class DeployCheckpointRequestDto
{
    public string ModelId { get; set; } = "";
    public string CheckpointId { get; set; } = "";
}

public sealed class DeployPreviewDto
{
    public CheckpointRowDto? CurrentProduction { get; init; }
    public CheckpointRowDto? Candidate { get; init; }
    public List<CheckpointCompareRowDto> Comparison { get; init; } = [];
}

public sealed class TrainingHistoryRowDto
{
    public Guid JobId { get; init; }
    public string ModelId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public string Duration { get; init; } = "";
    public string Dataset { get; init; } = "";
    public string? User { get; init; }
    public string Status { get; init; } = "";
    public string? FailureReason { get; init; }
    public string? CheckpointPath { get; init; }
    public string? LogPath { get; init; }
    public string Hyperparameters { get; init; } = "";
}

public sealed class SystemStatusDto
{
    public double CpuUsagePercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double DiskFreeGb { get; init; }
    public double DiskTotalGb { get; init; }
    public double DatasetStorageGb { get; init; }
    public int CheckpointCount { get; init; }
    public string PythonVersion { get; init; } = "";
    public string TorchVersion { get; init; } = "";
    public bool CudaAvailable { get; init; }
    public string? GpuName { get; init; }
    public double? GpuUsagePercent { get; init; }
}
