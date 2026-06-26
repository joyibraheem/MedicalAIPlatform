using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

public sealed class PredictionFeedbackService
{
    private const int MaxOriginalJsonChars = 400_000;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ModelTrainingDataService _trainingData;
    private readonly ModelRetrainingOrchestrator _retraining;

    public PredictionFeedbackService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        ModelTrainingDataService trainingData,
        ModelRetrainingOrchestrator retraining)
    {
        _db = db;
        _users = users;
        _trainingData = trainingData;
        _retraining = retraining;
    }

    private void QueueAudit(Guid feedbackId, string actorUserId, string action, object? detail)
    {
        var json = detail is null ? null : JsonSerializer.Serialize(detail, JsonOpts);
        _db.PredictionFeedbackAuditEntries.Add(new PredictionFeedbackAuditEntry
        {
            PredictionFeedbackId = feedbackId,
            ActorUserId = actorUserId,
            Action = action,
            DetailJson = json,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task<(bool Ok, string Message, Guid? Id)> SubmitAsync(string doctorUserId, SubmitPredictionFeedbackDto dto,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Modality) || string.IsNullOrWhiteSpace(dto.ModelKey))
            return (false, "Modality and model key are required.", null);

        if (dto.ClientSessionCorrelationId == Guid.Empty)
            return (false, "Correlation id is required.", null);

        var action = dto.DoctorAction?.Trim() ?? "";
        if (action != PredictionFeedbackStatuses.DoctorAccept && action != PredictionFeedbackStatuses.DoctorModify)
            return (false, "DoctorAction must be Accept or Modify.", null);

        if (dto.OriginalPredictionJson.Length > MaxOriginalJsonChars)
            return (false, "Original prediction payload is too large.", null);

        if (action == PredictionFeedbackStatuses.DoctorModify)
        {
            var hasLabel = !string.IsNullOrWhiteSpace(dto.CorrectedPrimaryLabel);
            var hasProbs = dto.CorrectedProbabilities is { Count: > 0 };
            var hasNotes = !string.IsNullOrWhiteSpace(dto.ClinicalNotes);
            if (!hasLabel && !hasProbs && !hasNotes)
                return (false, "Modify requires a corrected label, probabilities, and/or clinical notes.", null);
        }

        var pending = await _db.PredictionFeedbacks
            .Where(f => f.SubmittingDoctorUserId == doctorUserId
                        && f.ClientSessionCorrelationId == dto.ClientSessionCorrelationId
                        && f.Modality == dto.Modality
                        && f.ModelKey == dto.ModelKey
                        && f.ReviewStatus == PredictionFeedbackStatuses.ReviewPending)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var probsJson = dto.CorrectedProbabilities is { Count: > 0 }
            ? JsonSerializer.Serialize(dto.CorrectedProbabilities, JsonOpts)
            : null;

        if (pending is not null)
        {
            pending.OriginalPredictionJson = dto.OriginalPredictionJson;
            pending.DoctorAction = action;
            pending.CorrectedPrimaryLabel = dto.CorrectedPrimaryLabel?.Trim();
            pending.CorrectedPrimaryConfidence = dto.CorrectedPrimaryConfidence is >= 0 and <= 1
                ? dto.CorrectedPrimaryConfidence
                : dto.CorrectedPrimaryConfidence is null ? null : Math.Clamp(dto.CorrectedPrimaryConfidence.Value, 0, 1);
            pending.CorrectedProbabilitiesJson = probsJson;
            pending.ClinicalNotes = dto.ClinicalNotes?.Trim();
            pending.RelatedJobId = dto.RelatedJobId;
            pending.StudyInstanceUid = NullIfLong(dto.StudyInstanceUid, 128);
            pending.SeriesInstanceUid = NullIfLong(dto.SeriesInstanceUid, 128);
            pending.TrainingAssetPointerJson = dto.TrainingAssetPointerJson?.Trim();

            QueueAudit(pending.Id, doctorUserId, PredictionFeedbackStatuses.AuditUpdated, new { dto.DoctorAction });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await _trainingData.RecordFromFeedbackAsync(pending, ct).ConfigureAwait(false);
            QueueRetrainingIfModified(pending);
            return (true, "Feedback updated.", pending.Id);
        }

        var row = new PredictionFeedback
        {
            Id = Guid.NewGuid(),
            SubmittingDoctorUserId = doctorUserId,
            RelatedJobId = dto.RelatedJobId,
            ClientSessionCorrelationId = dto.ClientSessionCorrelationId,
            Modality = dto.Modality.Trim(),
            ModelKey = dto.ModelKey.Trim(),
            StudyInstanceUid = NullIfLong(dto.StudyInstanceUid, 128),
            SeriesInstanceUid = NullIfLong(dto.SeriesInstanceUid, 128),
            OriginalPredictionJson = dto.OriginalPredictionJson,
            DoctorAction = action,
            CorrectedPrimaryLabel = dto.CorrectedPrimaryLabel?.Trim(),
            CorrectedPrimaryConfidence = dto.CorrectedPrimaryConfidence is >= 0 and <= 1
                ? dto.CorrectedPrimaryConfidence
                : dto.CorrectedPrimaryConfidence is null ? null : Math.Clamp(dto.CorrectedPrimaryConfidence.Value, 0, 1),
            CorrectedProbabilitiesJson = probsJson,
            ClinicalNotes = dto.ClinicalNotes?.Trim(),
            TrainingAssetPointerJson = dto.TrainingAssetPointerJson?.Trim(),
            ReviewStatus = PredictionFeedbackStatuses.ReviewPending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.PredictionFeedbacks.Add(row);
        QueueAudit(row.Id, doctorUserId, PredictionFeedbackStatuses.AuditSubmitted,
            new { row.DoctorAction, row.Modality, row.ModelKey });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _trainingData.RecordFromFeedbackAsync(row, ct).ConfigureAwait(false);
        QueueRetrainingIfModified(row);

        return (true, "Feedback submitted for review.", row.Id);
    }

    private void QueueRetrainingIfModified(PredictionFeedback feedback)
    {
        if (feedback.DoctorAction != PredictionFeedbackStatuses.DoctorModify)
            return;

        var modelName = ModelTrainingNames.NormalizeModelKey(feedback.ModelKey);
        if (modelName is null)
            return;

        _retraining.QueueRetrainingCheck(modelName);
    }

    public async Task<IReadOnlyList<PredictionFeedbackListItemDto>> GetPendingAsync(int skip, int take,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var rows = await _db.PredictionFeedbacks.AsNoTracking()
            .Where(f => f.ReviewStatus == PredictionFeedbackStatuses.ReviewPending)
            .OrderByDescending(f => f.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct).ConfigureAwait(false);

        var ids = rows.Select(r => r.SubmittingDoctorUserId).Distinct().ToList();
        var emails = await _users.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct).ConfigureAwait(false);
        var emailMap = emails.ToDictionary(e => e.Id, e => e.Email);

        return rows.Select(r => new PredictionFeedbackListItemDto
        {
            Id = r.Id,
            SubmittingDoctorUserId = r.SubmittingDoctorUserId,
            SubmittingDoctorEmail = emailMap.GetValueOrDefault(r.SubmittingDoctorUserId),
            Modality = r.Modality,
            ModelKey = r.ModelKey,
            DoctorAction = r.DoctorAction,
            ReviewStatus = r.ReviewStatus,
            CreatedAt = r.CreatedAt,
            OriginalPredictionJson = r.OriginalPredictionJson,
            CorrectedPrimaryLabel = r.CorrectedPrimaryLabel,
            CorrectedPrimaryConfidence = r.CorrectedPrimaryConfidence,
            CorrectedProbabilitiesJson = r.CorrectedProbabilitiesJson,
            ClinicalNotes = r.ClinicalNotes,
            RelatedJobId = r.RelatedJobId,
            ClientSessionCorrelationId = r.ClientSessionCorrelationId
        }).ToList();
    }

    public async Task<(bool Ok, string Message)> ApproveAsync(Guid id, string reviewerUserId, string? notes,
        CancellationToken ct = default)
    {
        var row = await _db.PredictionFeedbacks.FirstOrDefaultAsync(f => f.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return (false, "Feedback not found.");

        if (row.ReviewStatus != PredictionFeedbackStatuses.ReviewPending)
            return (false, "Only pending items can be approved.");

        row.ReviewStatus = PredictionFeedbackStatuses.ReviewApproved;
        row.ReviewedByUserId = reviewerUserId;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewNotes = notes?.Trim();

        QueueAudit(row.Id, reviewerUserId, PredictionFeedbackStatuses.AuditApproved, new { notes });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Approved.");
    }

    public async Task<(bool Ok, string Message)> RejectAsync(Guid id, string reviewerUserId, string? notes,
        CancellationToken ct = default)
    {
        var row = await _db.PredictionFeedbacks.FirstOrDefaultAsync(f => f.Id == id, ct).ConfigureAwait(false);
        if (row is null)
            return (false, "Feedback not found.");

        if (row.ReviewStatus != PredictionFeedbackStatuses.ReviewPending)
            return (false, "Only pending items can be rejected.");

        row.ReviewStatus = PredictionFeedbackStatuses.ReviewRejected;
        row.ReviewedByUserId = reviewerUserId;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        row.ReviewNotes = notes?.Trim();

        QueueAudit(row.Id, reviewerUserId, PredictionFeedbackStatuses.AuditRejected, new { notes });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, "Rejected.");
    }

    /// <summary>Approved rows not yet exported — for offline training jobs.</summary>
    public async Task<IReadOnlyList<TrainingExportRowDto>> GetApprovedNotExportedAsync(CancellationToken ct = default)
    {
        var rows = await _db.PredictionFeedbacks.AsNoTracking()
            .Where(f => f.ReviewStatus == PredictionFeedbackStatuses.ReviewApproved && f.ExportedForTrainingAt == null)
            .OrderBy(f => f.ReviewedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var list = new List<TrainingExportRowDto>();
        foreach (var r in rows)
        {
            Dictionary<string, double>? goldProbs = null;
            if (!string.IsNullOrWhiteSpace(r.CorrectedProbabilitiesJson))
            {
                try
                {
                    goldProbs = JsonSerializer.Deserialize<Dictionary<string, double>>(r.CorrectedProbabilitiesJson);
                }
                catch { /* ignore malformed */ }
            }

            var goldLabel = !string.IsNullOrWhiteSpace(r.CorrectedPrimaryLabel)
                ? r.CorrectedPrimaryLabel!
                : ExtractPredictedLabel(r.ModelKey, r.OriginalPredictionJson);

            list.Add(new TrainingExportRowDto
            {
                FeedbackId = r.Id,
                Modality = r.Modality,
                ModelKey = r.ModelKey,
                GoldLabel = goldLabel,
                GoldConfidence = r.CorrectedPrimaryConfidence,
                GoldProbabilities = goldProbs,
                OriginalPredictionJson = r.OriginalPredictionJson,
                RelatedJobId = r.RelatedJobId,
                StudyInstanceUid = r.StudyInstanceUid,
                ClinicalNotes = r.ClinicalNotes,
                ApprovedAt = r.ReviewedAt ?? r.CreatedAt,
                TrainingAssetPointerJson = r.TrainingAssetPointerJson
            });
        }

        return list;
    }

    public async Task<(bool Ok, string Message)> MarkExportedAsync(string reviewerUserId, MarkExportedDto dto,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.BatchId))
            return (false, "BatchId is required.");

        var ids = dto.FeedbackIds.Distinct().ToList();
        if (ids.Count == 0)
            return (false, "No ids.");

        var rows = await _db.PredictionFeedbacks
            .Where(f => ids.Contains(f.Id)
                        && f.ReviewStatus == PredictionFeedbackStatuses.ReviewApproved
                        && f.ExportedForTrainingAt == null)
            .ToListAsync(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var r in rows)
        {
            r.ExportedForTrainingAt = now;
            r.TrainingExportBatchId = dto.BatchId.Trim();
            QueueAudit(r.Id, reviewerUserId, PredictionFeedbackStatuses.AuditExportMarked, new { dto.BatchId });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (true, $"Marked {rows.Count} row(s).");
    }

    private static string? NullIfLong(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>Best-effort gold label when doctor accepted without overriding label.</summary>
    private static string ExtractPredictedLabel(string modelKey, string originalJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(originalJson);
            var root = doc.RootElement;
            if (modelKey.Equals("LungAI", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("predicted_class", out var pc))
                return pc.GetString() ?? "";

            if (modelKey.Equals("CheXNet", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("probabilities", out var probs) && probs.ValueKind == JsonValueKind.Object)
                {
                    var best = probs.EnumerateObject()
                        .OrderByDescending(p => p.Value.GetDouble())
                        .FirstOrDefault();
                    return best.Name ?? "";
                }
            }

            if (modelKey.Equals("BioBERT", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("entities", out var ents) && ents.ValueKind == JsonValueKind.Array && ents.GetArrayLength() > 0)
            {
                return ents[0].TryGetProperty("word", out var w) ? w.GetString() ?? "" : "entities";
            }
        }
        catch { /* ignore */ }

        return "";
    }
}
