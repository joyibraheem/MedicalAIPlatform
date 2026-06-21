using System.Collections.Concurrent;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public sealed class UserAnalyticsSessionStore : IUserAnalyticsSessionStore
{
    private sealed class UserBucket
    {
        public AnalyticsSessionSnapshot Latest = AnalyticsSessionSnapshot.Empty;
        public ConcurrentDictionary<Guid, AnalyticsSessionSnapshot> ByJob { get; } = new();
        public object Gate { get; } = new();
    }

    private readonly ConcurrentDictionary<string, UserBucket> _users = new(StringComparer.Ordinal);

    public AnalyticsSessionSnapshot? GetLatest(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        if (!_users.TryGetValue(userId, out var bucket))
            return null;

        lock (bucket.Gate)
        {
            return IsEmpty(bucket.Latest) ? null : Clone(bucket.Latest);
        }
    }

    public void MergeLatest(string userId, AnalyticsSessionSnapshot patch)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        var bucket = _users.GetOrAdd(userId, _ => new UserBucket());
        lock (bucket.Gate)
        {
            bucket.Latest = AnalyticsSessionSnapshot.Merge(bucket.Latest, patch);
        }
    }

    public void SetLatest(string userId, AnalyticsSessionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        var bucket = _users.GetOrAdd(userId, _ => new UserBucket());
        lock (bucket.Gate)
        {
            bucket.Latest = snapshot;
        }
    }

    public AnalyticsSessionSnapshot? GetForJob(string userId, Guid jobId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        if (!_users.TryGetValue(userId, out var bucket))
            return null;

        return bucket.ByJob.TryGetValue(jobId, out var snap) ? Clone(snap) : null;
    }

    public void SetForJob(string userId, Guid jobId, AnalyticsSessionSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User id is required.", nameof(userId));

        var bucket = _users.GetOrAdd(userId, _ => new UserBucket());
        bucket.ByJob[jobId] = snapshot;
        MergeLatest(userId, snapshot);
    }

    public void Clear(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (_users.TryGetValue(userId, out var bucket))
        {
            lock (bucket.Gate)
            {
                bucket.Latest = AnalyticsSessionSnapshot.Empty;
            }

            bucket.ByJob.Clear();
        }
    }

    private static bool IsEmpty(AnalyticsSessionSnapshot snap) =>
        snap.CurrentResults is null or { Count: 0 }
        && snap.CurrentBioBertResults is null
        && snap.CurrentLungAIResults is null
        && string.IsNullOrWhiteSpace(snap.XRayImageDataUrl)
        && string.IsNullOrWhiteSpace(snap.CTImageDataUrl)
        && string.IsNullOrWhiteSpace(snap.ClinicalText)
        && snap.PipelineNotes.Count == 0;

    private static AnalyticsSessionSnapshot Clone(AnalyticsSessionSnapshot snap) =>
        AnalyticsSessionSnapshot.Merge(AnalyticsSessionSnapshot.Empty, snap);
}
