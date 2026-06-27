namespace MedicalAIPlatform.Models.TrainingCenter;

public static class TrainingJobStatusesExtended
{
    public const string Queued = "Queued";
    public const string Preparing = "Preparing";
    public const string Cancelled = "Cancelled";
}

public static class TrainingNotificationTypes
{
    public const string DatasetValidationPassed = "DatasetValidationPassed";
    public const string DatasetValidationFailed = "DatasetValidationFailed";
    public const string TrainingStarted = "TrainingStarted";
    public const string TrainingCompleted = "TrainingCompleted";
    public const string TrainingFailed = "TrainingFailed";
    public const string CheckpointSaved = "CheckpointSaved";
    public const string DeployCompleted = "DeployCompleted";
    public const string RollbackCompleted = "RollbackCompleted";
}

public sealed class TrainingCenterNotification
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ModelName { get; set; } = "";
    public string NotificationType { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "info";
    public bool IsRead { get; set; }
    public Guid? RelatedJobId { get; set; }
}

public sealed class DatasetArchiveEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string PathHash { get; set; } = "";
    public DateTimeOffset UploadDate { get; set; }
    public long SizeBytes { get; set; }
    public int ImageCount { get; set; }
    public int PatientCount { get; set; }
    public int NormalCount { get; set; }
    public int PositiveCount { get; set; }
    public string? DiseaseDistributionJson { get; set; }
    public string? UsedModelsJson { get; set; }
    public int TrainingRunCount { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public string? VersionLabel { get; set; }
    public string? ContentHash { get; set; }
    public string? ValidationStatus { get; set; }
}

public sealed class DeploymentHistoryRecord
{
    public Guid Id { get; set; }
    public string ModelName { get; set; } = "";
    public string? FromVersion { get; set; }
    public string ToVersion { get; set; } = "";
    public string ToCheckpointId { get; set; } = "";
    public DateTimeOffset DeployedAt { get; set; }
    public string? DeployedByUserId { get; set; }
    public string Action { get; set; } = "Deploy";
    public string? Reason { get; set; }
}

public static class HyperparameterPresets
{
    public static IReadOnlyList<HyperparameterPresetDto> All { get; } =
    [
        new HyperparameterPresetDto
        {
            Id = "quick-test",
            Name = "Quick Test",
            Description = "100 images · 2 epochs · fast smoke test",
            SubsetSize = 100,
            Epochs = 2,
            LearningRate = 1e-4,
            BatchSize = 8,
            ValRatio = 0.2,
            EarlyStoppingPatience = 0,
            NumWorkers = 0,
            FreezeBackbone = true,
        },
        new HyperparameterPresetDto
        {
            Id = "medium-training",
            Name = "Medium Training",
            Description = "600 images · CPU-friendly settings",
            SubsetSize = 600,
            Epochs = 5,
            LearningRate = 1e-4,
            BatchSize = 8,
            ValRatio = 0.2,
            EarlyStoppingPatience = 3,
            NumWorkers = 0,
            FreezeBackbone = true,
        },
        new HyperparameterPresetDto
        {
            Id = "full-training",
            Name = "Full Training",
            Description = "3000 images · full BRAX subset",
            SubsetSize = 3000,
            Epochs = 10,
            LearningRate = 5e-5,
            BatchSize = 8,
            ValRatio = 0.2,
            EarlyStoppingPatience = 5,
            NumWorkers = 0,
            FreezeBackbone = false,
            UnfreezeLastBlock = true,
        },
        new HyperparameterPresetDto
        {
            Id = "custom",
            Name = "Custom",
            Description = "User-defined values (no auto-fill override)",
            IsCustom = true,
        },
    ];
}
