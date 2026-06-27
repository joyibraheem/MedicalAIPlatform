using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class TrainingNotificationService
{
    private readonly ApplicationDbContext _db;

    public TrainingNotificationService(ApplicationDbContext db) => _db = db;

    public async Task NotifyAsync(
        string type,
        string modelName,
        string message,
        string severity = "info",
        Guid? jobId = null,
        CancellationToken ct = default)
    {
        try
        {
            _db.TrainingCenterNotifications.Add(new TrainingCenterNotification
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                ModelName = modelName,
                NotificationType = type,
                Message = message,
                Severity = severity,
                IsRead = false,
                RelatedJobId = jobId,
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Table may not exist until migration applied — fail silently for resilience.
        }
    }

    public async Task<List<TrainingNotificationDto>> ListAsync(int take = 50, CancellationToken ct = default)
    {
        try
        {
            return await _db.TrainingCenterNotifications.AsNoTracking()
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .Select(n => new TrainingNotificationDto
                {
                    Id = n.Id,
                    CreatedAt = n.CreatedAt,
                    ModelName = n.ModelName,
                    NotificationType = n.NotificationType,
                    Message = n.Message,
                    Severity = n.Severity,
                    IsRead = n.IsRead,
                })
                .ToListAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        try
        {
            return await _db.TrainingCenterNotifications.CountAsync(n => !n.IsRead, ct).ConfigureAwait(false);
        }
        catch
        {
            return 0;
        }
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var n = await _db.TrainingCenterNotifications.FindAsync([id], ct).ConfigureAwait(false);
        if (n is null) return;
        n.IsRead = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var unread = await _db.TrainingCenterNotifications.Where(n => !n.IsRead).ToListAsync(ct).ConfigureAwait(false);
        foreach (var n in unread) n.IsRead = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        _db.TrainingCenterNotifications.RemoveRange(await _db.TrainingCenterNotifications.ToListAsync(ct).ConfigureAwait(false));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

public sealed class DatasetArchiveService
{
    private readonly ApplicationDbContext _db;
    private readonly CheXNetPathResolver _paths;

    public DatasetArchiveService(ApplicationDbContext db, CheXNetPathResolver paths)
    {
        _db = db;
        _paths = paths;
    }

    public async Task<DatasetArchiveEntry> RegisterOrUpdateAsync(
        string path,
        string? name,
        DatasetValidationResultDto? validation = null,
        CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(path);
        var hash = ComputePathHash(fullPath);
        DatasetArchiveEntry? entry = null;
        try
        {
            entry = await _db.DatasetArchiveEntries
                .FirstOrDefaultAsync(d => d.PathHash == hash && !d.IsDeleted, ct).ConfigureAwait(false);
        }
        catch { }

        if (entry is null)
        {
            entry = new DatasetArchiveEntry
            {
                Id = Guid.NewGuid(),
                Path = fullPath,
                PathHash = hash,
                UploadDate = DateTimeOffset.UtcNow,
                Name = name ?? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
            };
            try { _db.DatasetArchiveEntries.Add(entry); } catch { return BuildFallback(fullPath, name); }
        }
        else
        {
            // Never overwrite immutable dataset paths — only refresh validation metadata.
            entry.SizeBytes = validation?.DatasetSizeBytes ?? entry.SizeBytes;
            if (validation is not null)
            {
                entry.ImageCount = validation.ValidRows;
                entry.PatientCount = validation.EstimatedPatients;
                entry.NormalCount = validation.NormalImages;
                entry.PositiveCount = validation.PositiveImages;
                entry.DiseaseDistributionJson = JsonSerializer.Serialize(validation.DiseaseDistribution);
                entry.ValidationStatus = validation.Ready ? "Validated" : "Failed";
            }
            try { await _db.SaveChangesAsync(ct).ConfigureAwait(false); } catch { }
            return entry;
        }

        entry.SizeBytes = validation?.DatasetSizeBytes ?? DirSize(fullPath);
        if (validation is not null)
        {
            entry.ImageCount = validation.ValidRows;
            entry.PatientCount = validation.EstimatedPatients;
            entry.NormalCount = validation.NormalImages;
            entry.PositiveCount = validation.PositiveImages;
            entry.DiseaseDistributionJson = JsonSerializer.Serialize(validation.DiseaseDistribution);
            entry.ValidationStatus = validation.Ready ? "Validated" : "Failed";
        }

        if (string.IsNullOrEmpty(entry.VersionLabel))
        {
            var prefix = fullPath.Contains("BRAX", StringComparison.OrdinalIgnoreCase) ? "BRAX" : "Dataset";
            try
            {
                var count = await _db.DatasetArchiveEntries.CountAsync(
                    d => d.VersionLabel != null && d.VersionLabel.StartsWith(prefix + "_v"), ct).ConfigureAwait(false);
                entry.VersionLabel = $"{prefix}_v{count + 1}";
            }
            catch
            {
                entry.VersionLabel = $"{prefix}_v1";
            }
        }

        try { await _db.SaveChangesAsync(ct).ConfigureAwait(false); } catch { }
        return entry;
    }

    public async Task<List<DatasetArchiveRowDto>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            var rows = await _db.DatasetArchiveEntries.AsNoTracking()
                .Where(d => !d.IsDeleted)
                .OrderByDescending(d => d.UploadDate)
                .ToListAsync(ct).ConfigureAwait(false);
            return rows.Select(ToDto).ToList();
        }
        catch
        {
            return ListFromFilesystem();
        }
    }

    public async Task RecordTrainingUseAsync(string datasetPath, string modelName, CancellationToken ct = default)
    {
        var hash = ComputePathHash(Path.GetFullPath(datasetPath));
        try
        {
            var entry = await _db.DatasetArchiveEntries.FirstOrDefaultAsync(d => d.PathHash == hash, ct).ConfigureAwait(false);
            if (entry is null) return;
            entry.TrainingRunCount++;
            var models = string.IsNullOrEmpty(entry.UsedModelsJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(entry.UsedModelsJson) ?? [];
            if (!models.Contains(modelName, StringComparer.OrdinalIgnoreCase))
                models.Add(modelName);
            entry.UsedModelsJson = JsonSerializer.Serialize(models);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch { }
    }

    public async Task<(bool ok, string message)> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _db.DatasetArchiveEntries.FindAsync([id], ct).ConfigureAwait(false);
        if (entry is null) return (false, "Dataset not found.");
        entry.IsDeleted = true;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Dataset archived entry marked deleted.");
    }

    public async Task<(bool ok, string message)> UpdateNotesAsync(Guid id, string notes, CancellationToken ct = default)
    {
        var entry = await _db.DatasetArchiveEntries.FindAsync([id], ct).ConfigureAwait(false);
        if (entry is null) return (false, "Dataset not found.");
        entry.Notes = notes;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Notes saved.");
    }

    private List<DatasetArchiveRowDto> ListFromFilesystem()
    {
        var list = new List<DatasetArchiveRowDto>();
        var brax = _paths.ResolveBraxRoot();
        if (Directory.Exists(brax))
        {
            list.Add(new DatasetArchiveRowDto
            {
                Id = Guid.Empty,
                Name = "BRAX",
                Path = brax,
                UploadDate = Directory.GetCreationTimeUtc(brax),
                SizeBytes = DirSize(brax),
            });
        }
        foreach (var dir in Directory.GetDirectories(_paths.ResolveDatasetUploadDirectory()))
        {
            list.Add(new DatasetArchiveRowDto
            {
                Id = Guid.Empty,
                Name = Path.GetFileName(dir),
                Path = dir,
                UploadDate = Directory.GetCreationTimeUtc(dir),
                SizeBytes = DirSize(dir),
            });
        }
        return list;
    }

    private static DatasetArchiveRowDto ToDto(DatasetArchiveEntry e)
    {
        var dist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(e.DiseaseDistributionJson))
        {
            try
            {
                dist = JsonSerializer.Deserialize<Dictionary<string, int>>(e.DiseaseDistributionJson)
                    ?? dist;
            }
            catch { }
        }
        var models = new List<string>();
        if (!string.IsNullOrEmpty(e.UsedModelsJson))
        {
            try { models = JsonSerializer.Deserialize<List<string>>(e.UsedModelsJson) ?? models; } catch { }
        }
        return new DatasetArchiveRowDto
        {
            Id = e.Id,
            Name = e.Name,
            Path = e.Path,
            UploadDate = e.UploadDate,
            SizeBytes = e.SizeBytes,
            ImageCount = e.ImageCount,
            PatientCount = e.PatientCount,
            NormalCount = e.NormalCount,
            PositiveCount = e.PositiveCount,
            DiseaseDistribution = dist,
            UsedModels = models,
            TrainingRuns = e.TrainingRunCount,
            Notes = e.Notes,
        };
    }

    private static DatasetArchiveEntry BuildFallback(string path, string? name) => new()
    {
        Id = Guid.NewGuid(),
        Path = path,
        Name = name ?? Path.GetFileName(path),
        PathHash = ComputePathHash(path),
        UploadDate = DateTimeOffset.UtcNow,
        SizeBytes = DirSize(path),
    };

    private static string ComputePathHash(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(bytes);
    }

    private static long DirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            size += f.Length;
        return size;
    }
}

public sealed class TrainingQueueService
{
    private readonly ApplicationDbContext _db;
    private readonly TrainingJobRuntimeStore _runtime;

    public TrainingQueueService(ApplicationDbContext db, TrainingJobRuntimeStore runtime)
    {
        _db = db;
        _runtime = runtime;
    }

    public async Task<List<TrainingQueueItemDto>> ListQueueAsync(CancellationToken ct = default)
    {
        var activeStatuses = new[]
        {
            TrainingJobStatuses.Queued,
            TrainingJobStatuses.Pending,
            TrainingJobStatuses.Preparing,
            TrainingJobStatuses.Running,
            "Training",
            "Preparing subset",
        };

        var jobs = await _db.TrainingJobs.AsNoTracking()
            .Where(j => activeStatuses.Contains(j.Status))
            .OrderBy(j => j.StartedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var queued = jobs.Where(j => j.Status is TrainingJobStatuses.Queued or TrainingJobStatuses.Pending).ToList();
        var result = new List<TrainingQueueItemDto>();
        var pos = 1;
        foreach (var j in jobs)
        {
            var isQueued = j.Status is TrainingJobStatuses.Queued or TrainingJobStatuses.Pending;
            var runtime = _runtime.Get(j.Id);
            result.Add(new TrainingQueueItemDto
            {
                JobId = j.Id,
                QueuePosition = isQueued ? pos++ : 0,
                ModelName = j.ModelName,
                Dataset = j.DatasetPath ?? "—",
                RequestedBy = j.RequestedByUserId,
                RequestedAt = j.StartedAt,
                Status = runtime?.Status ?? j.Status,
                CanCancel = isQueued || runtime?.Process is { HasExited: false },
            });
        }
        return result;
    }

    public async Task<(bool ok, string message)> CancelQueuedJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.TrainingJobs.FindAsync([jobId], ct).ConfigureAwait(false);
        if (job is null) return (false, "Job not found.");
        if (job.Status is not (TrainingJobStatuses.Queued or TrainingJobStatuses.Pending))
            return (false, "Only queued jobs can be cancelled this way.");

        job.Status = TrainingJobStatuses.Cancelled;
        job.FinishedAt = DateTimeOffset.UtcNow;
        var runtime = _runtime.Get(jobId);
        if (runtime is not null)
        {
            runtime.Status = TrainingJobStatuses.Cancelled;
            runtime.FinishedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Queued job cancelled.");
    }

    public async Task AssignQueuePositionsAsync(CancellationToken ct = default)
    {
        var queued = await _db.TrainingJobs
            .Where(j => j.Status == TrainingJobStatuses.Queued || j.Status == TrainingJobStatuses.Pending)
            .OrderBy(j => j.StartedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < queued.Count; i++)
        {
            queued[i].QueuePosition = i + 1;
            if (_runtime.GetActive() is not null && i > 0)
                queued[i].Status = TrainingJobStatuses.Queued;
        }
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
