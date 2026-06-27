namespace MedicalAIPlatform.Models.TrainingCenter;

public sealed class HyperparameterPresetDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsCustom { get; init; }
    public int SubsetSize { get; init; } = 100;
    public int Epochs { get; init; } = 2;
    public double LearningRate { get; init; } = 1e-4;
    public int BatchSize { get; init; } = 8;
    public double ValRatio { get; init; } = 0.2;
    public int EarlyStoppingPatience { get; init; }
    public int NumWorkers { get; init; }
    public bool FreezeBackbone { get; init; } = true;
    public bool UnfreezeLastBlock { get; init; }
}

public sealed class TrainingQueueItemDto
{
    public Guid JobId { get; init; }
    public int QueuePosition { get; init; }
    public string ModelName { get; init; } = "";
    public string Dataset { get; init; } = "";
    public string? RequestedBy { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public string Status { get; init; } = "";
    public bool CanCancel { get; init; }
}

public sealed class TrainingNotificationDto
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string ModelName { get; init; } = "";
    public string NotificationType { get; init; } = "";
    public string Message { get; init; } = "";
    public string Severity { get; init; } = "info";
    public bool IsRead { get; init; }
}

public sealed class DatasetArchiveRowDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public DateTimeOffset UploadDate { get; init; }
    public long SizeBytes { get; init; }
    public int ImageCount { get; init; }
    public int PatientCount { get; init; }
    public int NormalCount { get; init; }
    public int PositiveCount { get; init; }
    public Dictionary<string, int> DiseaseDistribution { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> UsedModels { get; init; } = [];
    public int TrainingRuns { get; init; }
    public string? Notes { get; init; }
}

public sealed class ExperimentRowDto
{
    public Guid JobId { get; init; }
    public string ExperimentName { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string Dataset { get; init; } = "";
    public int Epochs { get; init; }
    public double LearningRate { get; init; }
    public int BatchSize { get; init; }
    public double? TrainingTimeSeconds { get; init; }
    public int? TrainableParameters { get; init; }
    public int? FrozenParameters { get; init; }
    public double? ValLoss { get; init; }
    public double? Accuracy { get; init; }
    public double? Precision { get; init; }
    public double? Recall { get; init; }
    public double? F1 { get; init; }
    public double? RocAuc { get; init; }
    public string? CheckpointId { get; init; }
    public string Status { get; init; } = "";
}

public sealed class ExperimentCompareResultDto
{
    public List<ExperimentRowDto> Experiments { get; init; } = [];
    public List<CheckpointCompareRowDto> MetricComparison { get; init; } = [];
    public string? BestExperimentId { get; init; }
    public string? RecommendedExperimentId { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class DeployBestRequestDto
{
    public string ModelId { get; set; } = "";
    public string Metric { get; set; } = "ROC-AUC";
}

public sealed class DeploymentHistoryRowDto
{
    public Guid Id { get; init; }
    public string ModelName { get; init; } = "";
    public string? FromVersion { get; init; }
    public string ToVersion { get; init; } = "";
    public DateTimeOffset DeployedAt { get; init; }
    public string Action { get; init; } = "";
    public string? Reason { get; init; }
    public bool IsCurrentProduction { get; init; }
}

public sealed class ModelVersionTimelineRowDto
{
    public string Id { get; init; } = "";
    public string ModelId { get; init; } = "";
    public string VersionNumber { get; init; } = "";
    public DateTimeOffset TrainingDate { get; init; }
    public string Dataset { get; init; } = "";
    public int? Epochs { get; init; }
    public double? Accuracy { get; init; }
    public double? F1 { get; init; }
    public double? RocAuc { get; init; }
    public string DeploymentStatus { get; init; } = "";
    public string? Notes { get; init; }
}

public sealed class TrainingRecommendationDto
{
    public Guid? JobId { get; init; }
    public string ModelId { get; init; } = "";
    public CheckpointRowDto? CurrentProduction { get; init; }
    public CheckpointRowDto? Candidate { get; init; }
    public double? AccuracyDelta { get; init; }
    public double? F1Delta { get; init; }
    public double? RocAucDelta { get; init; }
    public double? TrainingTimeDeltaSeconds { get; init; }
    public string Recommendation { get; init; } = "";
    public string Explanation { get; init; } = "";
}

public sealed class LiveResourceMonitorDto
{
    public double CpuUsagePercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double RamUsagePercent { get; init; }
    public double DiskUsedPercent { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskTotalGb { get; init; }
    public double? GpuUsagePercent { get; init; }
    public double? VramUsagePercent { get; init; }
    public double? GpuTemperatureC { get; init; }
    public string? GpuName { get; init; }
    public double? TrainingSpeedImagesPerSec { get; init; }
    public double? EstimatedRemainingSeconds { get; init; }
    public int? CurrentEpoch { get; init; }
    public string? CurrentBatch { get; init; }
    public List<ChartPointDto> CpuHistory { get; init; } = [];
    public List<ChartPointDto> RamHistory { get; init; } = [];
    public List<ChartPointDto> GpuHistory { get; init; } = [];
}

public sealed class InferenceTestRequestDto
{
    public string ModelId { get; set; } = "CheXNet";
    public string Source { get; set; } = "production";
    public string? CheckpointPath { get; set; }
    public string? HeatmapClass { get; set; }
}

public sealed class InferenceTestResultDto
{
    public string Source { get; init; } = "";
    public string Label { get; init; } = "";
    public double InferenceTimeMs { get; init; }
    public List<InferenceLabelScoreDto> Predictions { get; init; } = [];
    public string? HeatmapBase64 { get; init; }
    public string? Error { get; init; }
}

public sealed class InferenceLabelScoreDto
{
    public string Label { get; init; } = "";
    public double Confidence { get; init; }
}

public sealed class InferenceCompareResultDto
{
    public InferenceTestResultDto? Production { get; init; }
    public InferenceTestResultDto? Candidate { get; init; }
}
