using MedicalAIPlatform.Models.TrainingCenter;

namespace MedicalAIPlatform.Models;

public sealed class FineTuningDashboardViewModel
{
    public TrainingCenterDashboardDto Dashboard { get; init; } = new();

    public int LiveRefreshSeconds { get; init; } = 5;

    public string InitialJson { get; init; } = "{}";

    public int ModifiedThreshold { get; init; }

    public List<FineTuningModelSummaryVm> Models { get; init; } = [];

    public List<TrainingJobListItemVm> PendingJobs { get; init; } = [];

    public List<TrainingJobListItemVm> RecentJobs { get; init; } = [];
}

public sealed class FineTuningModelSummaryVm
{
    public string ModelName { get; init; } = "";

    public int AcceptedCount { get; init; }

    public int ModifiedCount { get; init; }

    public int UnprocessedModifiedCount { get; init; }

    public string? CurrentVersion { get; init; }

    public DateTimeOffset? LastTrainingDate { get; init; }

    public int? LastDatasetSize { get; init; }

    public double? ProductionAccuracy { get; init; }

    public double? ProductionF1Score { get; init; }

    public int DeployableVersionsCount { get; init; }

    public bool RetrainingReady { get; init; }

    public int ModifiedThreshold { get; init; }
}

public sealed class TrainingJobListItemVm
{
    public Guid Id { get; init; }

    public string ModelName { get; init; } = "";

    public int DatasetSize { get; init; }

    public string Status { get; init; } = "";

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    public string? PreviousModelVersion { get; init; }

    public string? NewModelVersion { get; init; }

    public double? Accuracy { get; init; }

    public double? F1Score { get; init; }

    public double? Loss { get; init; }

    public string? ErrorMessage { get; init; }
}
