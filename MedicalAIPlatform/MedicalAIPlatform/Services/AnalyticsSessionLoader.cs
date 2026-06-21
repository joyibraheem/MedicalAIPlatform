using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

/// <summary>Loads analytics view state for the current user from memory or persisted job payloads.</summary>
public sealed class AnalyticsSessionLoader
{
    private readonly IUserAnalyticsSessionStore _store;
    private readonly ApplicationDbContext _db;

    public AnalyticsSessionLoader(IUserAnalyticsSessionStore store, ApplicationDbContext db)
    {
        _store = store;
        _db = db;
    }

    public async Task<AnalyticsSessionSnapshot?> LoadAsync(string userId, Guid? jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        if (jobId is { } jid)
        {
            var fromMemory = _store.GetForJob(userId, jid);
            if (fromMemory is not null)
                return fromMemory;

            var fromDb = await LoadViewStateFromJobAsync(userId, jid, cancellationToken).ConfigureAwait(false);
            if (fromDb is not null)
            {
                _store.SetForJob(userId, jid, fromDb);
                return fromDb;
            }

            return null;
        }

        return _store.GetLatest(userId);
    }

    private async Task<AnalyticsSessionSnapshot?> LoadViewStateFromJobAsync(string userId, Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _db.ChestAiBackgroundJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (job is null || string.IsNullOrWhiteSpace(job.ResultPayloadJson))
            return null;

        if (!AnalyticsJobResultEnvelope.TryParse(job.ResultPayloadJson, out _, out var viewState))
            return null;

        return viewState;
    }
}
