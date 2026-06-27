namespace MedicalAIPlatform.Models.TrainingCenter;

public sealed class DashboardHomeSummaryDto
{
    public int ModelCount { get; init; }
    public int DatasetCount { get; init; }
    public int RunningJobs { get; init; }
    public int QueuedJobs { get; init; }
    public int FinishedJobs { get; init; }
    public int DeployableModels { get; init; }
    public int ProductionModels { get; init; }
    public double StorageUsedGb { get; init; }
    public string? LatestTraining { get; init; }
    public string? LatestDeployment { get; init; }
}

public sealed class TrainingWizardStateDto
{
    public List<TrainingWizardStepDto> Steps { get; init; } = [];
    public int CurrentStep { get; init; } = 1;
}

public sealed class TrainingWizardStepDto
{
    public int Step { get; init; }
    public string Title { get; init; } = "";
    public string Status { get; init; } = "pending";
}

public sealed class DatasetPreviewDto
{
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public long SizeBytes { get; init; }
    public int ImageCount { get; init; }
    public int PatientCount { get; init; }
    public int NormalCount { get; init; }
    public int PositiveCount { get; init; }
    public int DiseaseCount { get; init; }
    public DateTimeOffset? CreatedDate { get; init; }
    public string? ValidationStatus { get; init; }
    public string? ContentHash { get; init; }
    public List<DatasetPreviewRowDto> SampleRows { get; init; } = [];
}

public sealed class DatasetPreviewRowDto
{
    public string PatientId { get; init; } = "";
    public string StudyId { get; init; } = "";
    public string ImageName { get; init; } = "";
    public string Labels { get; init; } = "";
    public string ViewPosition { get; init; } = "";
    public string DicomPath { get; init; } = "";
    public string PngPath { get; init; } = "";
}

public sealed class DatasetStatisticsDto
{
    public Dictionary<string, int> DiseaseDistribution { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public int NormalCount { get; init; }
    public int PositiveCount { get; init; }
    public List<ChartPointDto> ImagesPerPatient { get; init; } = [];
    public List<ChartPointDto> TrainVsValidation { get; init; } = [];
    public List<ChartPointDto> LabelFrequency { get; init; } = [];
    public double ClassImbalanceRatio { get; init; }
    public List<SummaryCardDto> SummaryCards { get; init; } = [];
}

public sealed class SummaryCardDto
{
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class DatasetExplorerResultDto
{
    public int TotalMatches { get; init; }
    public List<DatasetPreviewRowDto> Rows { get; init; } = [];
}

public sealed class DatasetExplorerQueryDto
{
    public string DatasetPath { get; set; } = "";
    public string? PatientId { get; set; }
    public string? Study { get; set; }
    public string? Disease { get; set; }
    public string? Image { get; set; }
    public string? Label { get; set; }
    public string? Date { get; set; }
    public string SortBy { get; set; } = "PatientID";
    public bool SortDesc { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
}

public sealed class ModelArchitectureDto
{
    public string ModelId { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string Backbone { get; init; } = "";
    public string Classifier { get; init; } = "";
    public string InputSize { get; init; } = "";
    public int OutputClasses { get; init; }
    public long? TrainableParameters { get; init; }
    public long? FrozenParameters { get; init; }
    public long? TotalParameters { get; init; }
    public string TrainingStrategy { get; init; } = "";
    public string TransferLearningStrategy { get; init; } = "";
    public List<string> DiagramLayers { get; init; } = [];
}

public sealed class TrainingConsoleDto
{
    public Guid? JobId { get; init; }
    public string LogText { get; init; } = "";
    public bool IsRunning { get; init; }
}

public sealed class ThresholdAnalysisDto
{
    public double Threshold { get; init; }
    public double? Accuracy { get; init; }
    public double? Precision { get; init; }
    public double? Recall { get; init; }
    public double? F1 { get; init; }
    public double? Specificity { get; init; }
    public double? Sensitivity { get; init; }
    public int? PredictedPositives { get; init; }
    public int? FalsePositives { get; init; }
    public int? FalseNegatives { get; init; }
    public List<ChartPointDto> F1Curve { get; init; } = [];
}

public sealed class CheckpointDetailsDto
{
    public CheckpointRowDto Checkpoint { get; init; } = new();
    public string? Optimizer { get; init; }
    public double? LearningRate { get; init; }
    public int? Epochs { get; init; }
    public string? Dataset { get; init; }
    public string? DatasetVersion { get; init; }
    public string? DatasetHash { get; init; }
    public string? ArchitectureSummary { get; init; }
    public long? TrainableParameters { get; init; }
    public long? FrozenParameters { get; init; }
    public TrainingChartsDto Charts { get; init; } = new();
    public ModelArchitectureDto? Architecture { get; init; }
    public Dictionary<string, string> DownloadLinks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Notes { get; init; }
    public CheckpointFlagsDto Flags { get; init; } = new();
    public TrainingRecommendationDto? Recommendation { get; init; }
}

public sealed class CheckpointFlagsDto
{
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
    public bool IsProductionCandidate { get; init; }
    public bool IsRecommended { get; init; }
}

public sealed class CheckpointFlagsUpdateDto
{
    public string CheckpointId { get; set; } = "";
    public bool? IsFavorite { get; set; }
    public bool? IsPinned { get; set; }
    public bool? IsProductionCandidate { get; set; }
    public bool? IsRecommended { get; set; }
}

public sealed class CheckpointNotesUpdateDto
{
    public string CheckpointId { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class WorkflowTimelineDto
{
    public List<WorkflowStageDto> Stages { get; init; } = [];
    public string CurrentStageId { get; init; } = "";
}

public sealed class WorkflowStageDto
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Status { get; init; } = "pending";
}

public sealed class ProductionIntegrationTestResultDto
{
    public string ModelVersion { get; init; } = "";
    public string TopPrediction { get; init; } = "";
    public double Confidence { get; init; }
    public double InferenceTimeMs { get; init; }
    public List<InferenceLabelScoreDto> Predictions { get; init; } = [];
    public string? Error { get; init; }
}

public sealed class EnhancedRecommendationDto
{
    public TrainingRecommendationDto? Base { get; init; }
    public List<string> Insights { get; init; } = [];
    public string RecommendedAction { get; init; } = "";
}

public sealed class DatasetVersionRowDto
{
    public string Version { get; init; } = "";
    public string Path { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public DateTimeOffset CreatedDate { get; init; }
    public string ValidationStatus { get; init; } = "";
}

public interface IModelPluginMetadataProvider
{
    string ModelId { get; }
    ModelArchitectureDto GetArchitecture();
    Task<DatasetPreviewDto?> BuildDatasetPreviewAsync(string datasetPath, CancellationToken ct = default);
    Task<DatasetStatisticsDto?> BuildDatasetStatisticsAsync(string datasetPath, CancellationToken ct = default);
}
