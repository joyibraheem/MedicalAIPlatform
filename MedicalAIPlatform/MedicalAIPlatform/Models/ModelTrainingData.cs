namespace MedicalAIPlatform.Models;

public static class ModelTrainingNames
{
    public const string CheXNet = "CheXNet";
    public const string LungCancer = "LungCancer";
    public const string BioBERT = "BioBERT";

    public static string? NormalizeModelKey(string modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
            return null;

        if (modelKey.Equals(CheXNet, StringComparison.OrdinalIgnoreCase))
            return CheXNet;
        if (modelKey.Equals("LungAI", StringComparison.OrdinalIgnoreCase)
            || modelKey.Equals(LungCancer, StringComparison.OrdinalIgnoreCase))
            return LungCancer;
        if (modelKey.Equals(BioBERT, StringComparison.OrdinalIgnoreCase))
            return BioBERT;

        return null;
    }
}

public static class TrainingJobStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Queued = "Queued";
    public const string Preparing = "Preparing";
    public const string Cancelled = "Cancelled";
}

/// <summary>Shared columns for model-specific accepted-data tables.</summary>
public abstract class ModelAcceptedDataBase
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public string InputDataReference { get; set; } = "";
    public string Prediction { get; set; } = "";
    public double? ConfidenceScore { get; set; }
    public string DoctorId { get; set; } = "";
    public int? PatientId { get; set; }
    public string? DicomStudyUid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SourceFeedbackId { get; set; }
    public Guid? TrainingBatchId { get; set; }
    public bool IsProcessed { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>JSON blob with image/DICOM/text paths and UIDs.</summary>
    public string? SourceDataJson { get; set; }
}

public sealed class CheXNetAcceptedData : ModelAcceptedDataBase;

public sealed class LungCancerAcceptedData : ModelAcceptedDataBase;

public sealed class BioBERTAcceptedData : ModelAcceptedDataBase;

public abstract class ModelModifiedDataBase
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public string OriginalPrediction { get; set; } = "";
    public string CorrectedPrediction { get; set; } = "";
    public double? OriginalConfidence { get; set; }
    public string? DoctorNotes { get; set; }
    public string DoctorId { get; set; } = "";
    public int? PatientId { get; set; }
    public string? DicomStudyUid { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? SourceFeedbackId { get; set; }
    public Guid? TrainingBatchId { get; set; }
    public bool IsProcessed { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    public string? SourceDataJson { get; set; }
}

public sealed class CheXNetModifiedData : ModelModifiedDataBase;

public sealed class LungCancerModifiedData : ModelModifiedDataBase;

public sealed class BioBERTModifiedData : ModelModifiedDataBase;

public sealed class TrainingJob
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public int DatasetSize { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string Status { get; set; } = TrainingJobStatuses.Pending;
    public string? PreviousModelVersion { get; set; }
    public string? NewModelVersion { get; set; }
    public double? Accuracy { get; set; }
    public double? F1Score { get; set; }
    public double? Loss { get; set; }
    public string? DatasetPath { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid TrainingBatchId { get; set; }
    public string? TrainingLogPath { get; set; }
    public string? RequestedByUserId { get; set; }
    public int? QueuePosition { get; set; }
    public string? HyperparametersJson { get; set; }
    public int? EpochsConfigured { get; set; }
    public double? LearningRate { get; set; }
    public int? BatchSizeConfigured { get; set; }
    public string? ExperimentName { get; set; }
}

public sealed class ModelVersion
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public DateTimeOffset TrainingDate { get; set; }
    public int DatasetSize { get; set; }
    public double? Accuracy { get; set; }
    public double? F1Score { get; set; }
    public double? Loss { get; set; }
    public string FilePath { get; set; } = "";
    public bool IsProduction { get; set; }
    public bool IsDeployable { get; set; }
    public Guid? TrainingJobId { get; set; }
    public string? Notes { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public bool IsProductionCandidate { get; set; }
    public bool IsRecommended { get; set; }
    public string? DatasetVersion { get; set; }
    public string? DatasetHash { get; set; }
    public string? ValidationStatus { get; set; }
    public string? TrainingReportPath { get; set; }
}
