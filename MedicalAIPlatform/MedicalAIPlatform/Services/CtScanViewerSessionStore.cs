using System.Collections.Concurrent;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public interface ICtScanViewerSessionStore
{
    void Save(CtScanViewerSession session);
    CtScanViewerSession? Get(Guid sessionId, string userId);
    bool Remove(Guid sessionId, string userId);
    void PurgeExpired(TimeSpan maxAge);
}

public sealed class CtScanViewerSessionStore : ICtScanViewerSessionStore
{
    private readonly ConcurrentDictionary<Guid, CtScanViewerSession> _sessions = new();

    public void Save(CtScanViewerSession session) => _sessions[session.SessionId] = session;

    public CtScanViewerSession? Get(Guid sessionId, string userId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return null;

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
            return null;

        return session;
    }

    public bool Remove(Guid sessionId, string userId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
            return false;

        _sessions.TryRemove(sessionId, out _);
        TryDeleteDirectory(session.TempDirectory);
        return true;
    }

    public void PurgeExpired(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        foreach (var kv in _sessions)
        {
            if (kv.Value.CreatedAt >= cutoff)
                continue;

            if (_sessions.TryRemove(kv.Key, out var removed))
                TryDeleteDirectory(removed.TempDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            /* best-effort temp cleanup */
        }
    }
}
