using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

/// <summary>Thread-safe per-user analytics session storage (single-instance; Redis-ready abstraction).</summary>
public interface IUserAnalyticsSessionStore
{
    AnalyticsSessionSnapshot? GetLatest(string userId);

    void MergeLatest(string userId, AnalyticsSessionSnapshot patch);

    void SetLatest(string userId, AnalyticsSessionSnapshot snapshot);

    AnalyticsSessionSnapshot? GetForJob(string userId, Guid jobId);

    void SetForJob(string userId, Guid jobId, AnalyticsSessionSnapshot snapshot);

    void Clear(string userId);
}
