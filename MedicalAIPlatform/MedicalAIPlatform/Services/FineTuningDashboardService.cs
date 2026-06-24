using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MedicalAIPlatform.Options;

namespace MedicalAIPlatform.Services;

public sealed class FineTuningDashboardService
{
    private readonly ApplicationDbContext _db;
    private readonly ModelRetrainingOptions _options;

    public FineTuningDashboardService(ApplicationDbContext db, IOptions<ModelRetrainingOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<FineTuningDashboardViewModel> BuildAsync(CancellationToken ct = default)
    {
        var models = new[]
        {
            ModelTrainingNames.CheXNet,
            ModelTrainingNames.LungCancer,
            ModelTrainingNames.BioBERT
        };

        var summaries = new List<FineTuningModelSummaryVm>();
        foreach (var model in models)
            summaries.Add(await BuildModelSummaryAsync(model, ct).ConfigureAwait(false));

        var pendingJobs = await _db.TrainingJobs.AsNoTracking()
            .Where(j => j.Status == TrainingJobStatuses.Pending || j.Status == TrainingJobStatuses.Running)
            .OrderByDescending(j => j.StartedAt)
            .Take(20)
            .ToListAsync(ct).ConfigureAwait(false);

        var recentJobs = await _db.TrainingJobs.AsNoTracking()
            .Where(j => j.Status == TrainingJobStatuses.Completed || j.Status == TrainingJobStatuses.Failed)
            .OrderByDescending(j => j.FinishedAt ?? j.StartedAt)
            .Take(20)
            .ToListAsync(ct).ConfigureAwait(false);

        return new FineTuningDashboardViewModel
        {
            ModifiedThreshold = _options.ModifiedThreshold,
            Models = summaries,
            PendingJobs = pendingJobs.Select(ToListItem).ToList(),
            RecentJobs = recentJobs.Select(ToListItem).ToList()
        };
    }

    private async Task<FineTuningModelSummaryVm> BuildModelSummaryAsync(string modelName, CancellationToken ct)
    {
        var (accepted, modified, unprocessedModified) = modelName switch
        {
            ModelTrainingNames.CheXNet => (
                await _db.CheXNetAcceptedData.CountAsync(ct).ConfigureAwait(false),
                await _db.CheXNetModifiedData.CountAsync(ct).ConfigureAwait(false),
                await _db.CheXNetModifiedData.CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false)),
            ModelTrainingNames.LungCancer => (
                await _db.LungCancerAcceptedData.CountAsync(ct).ConfigureAwait(false),
                await _db.LungCancerModifiedData.CountAsync(ct).ConfigureAwait(false),
                await _db.LungCancerModifiedData.CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false)),
            ModelTrainingNames.BioBERT => (
                await _db.BioBERTAcceptedData.CountAsync(ct).ConfigureAwait(false),
                await _db.BioBERTModifiedData.CountAsync(ct).ConfigureAwait(false),
                await _db.BioBERTModifiedData.CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false)),
            _ => (0, 0, 0)
        };

        var production = await _db.ModelVersions.AsNoTracking()
            .Where(v => v.ModelName == modelName && v.IsProduction)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var lastTraining = await _db.TrainingJobs.AsNoTracking()
            .Where(j => j.ModelName == modelName && j.Status == TrainingJobStatuses.Completed)
            .OrderByDescending(j => j.FinishedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var deployableCount = await _db.ModelVersions.AsNoTracking()
            .CountAsync(v => v.ModelName == modelName && v.IsDeployable && !v.IsProduction, ct)
            .ConfigureAwait(false);

        return new FineTuningModelSummaryVm
        {
            ModelName = modelName,
            AcceptedCount = accepted,
            ModifiedCount = modified,
            UnprocessedModifiedCount = unprocessedModified,
            CurrentVersion = production?.VersionNumber ?? "1.0",
            LastTrainingDate = lastTraining?.FinishedAt ?? production?.TrainingDate,
            LastDatasetSize = lastTraining?.DatasetSize ?? production?.DatasetSize,
            ProductionAccuracy = production?.Accuracy,
            ProductionF1Score = production?.F1Score,
            DeployableVersionsCount = deployableCount,
            RetrainingReady = unprocessedModified >= _options.GetThreshold(modelName),
            ModifiedThreshold = _options.GetThreshold(modelName)
        };
    }

    private static TrainingJobListItemVm ToListItem(TrainingJob j) => new()
    {
        Id = j.Id,
        ModelName = j.ModelName,
        DatasetSize = j.DatasetSize,
        Status = j.Status,
        StartedAt = j.StartedAt,
        FinishedAt = j.FinishedAt,
        PreviousModelVersion = j.PreviousModelVersion,
        NewModelVersion = j.NewModelVersion,
        Accuracy = j.Accuracy,
        F1Score = j.F1Score,
        Loss = j.Loss,
        ErrorMessage = j.ErrorMessage
    };
}
