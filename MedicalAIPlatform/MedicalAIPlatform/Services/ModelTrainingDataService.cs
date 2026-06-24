using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

/// <summary>Persists doctor Accept/Modify feedback into model-specific training tables.</summary>
public sealed class ModelTrainingDataService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly ApplicationDbContext _db;
    private readonly ILogger<ModelTrainingDataService> _logger;

    public ModelTrainingDataService(ApplicationDbContext db, ILogger<ModelTrainingDataService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordFromFeedbackAsync(PredictionFeedback feedback, CancellationToken ct = default)
    {
        var modelName = ModelTrainingNames.NormalizeModelKey(feedback.ModelKey);
        if (modelName is null)
            return;

        var patientId = await ResolvePatientIdAsync(feedback.RelatedJobId, ct).ConfigureAwait(false);
        var inputRef = BuildInputReference(feedback);
        var sourceDataJson = BuildSourceDataJson(feedback, patientId);
        var (prediction, confidence) = ExtractOriginalPrediction(feedback.ModelKey, feedback.OriginalPredictionJson);

        if (feedback.DoctorAction == PredictionFeedbackStatuses.DoctorAccept)
        {
            var goldLabel = !string.IsNullOrWhiteSpace(feedback.CorrectedPrimaryLabel)
                ? feedback.CorrectedPrimaryLabel!
                : prediction;
            var goldConf = feedback.CorrectedPrimaryConfidence ?? confidence;

            await UpsertAcceptedAsync(
                modelName,
                feedback,
                inputRef,
                sourceDataJson,
                goldLabel,
                goldConf,
                patientId,
                ct).ConfigureAwait(false);
        }
        else if (feedback.DoctorAction == PredictionFeedbackStatuses.DoctorModify)
        {
            var corrected = !string.IsNullOrWhiteSpace(feedback.CorrectedPrimaryLabel)
                ? feedback.CorrectedPrimaryLabel!
                : prediction;

            await UpsertModifiedAsync(
                modelName,
                feedback,
                sourceDataJson,
                prediction,
                corrected,
                confidence,
                patientId,
                ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Training data recorded for {Model} ({Action}), feedback {FeedbackId}",
            modelName,
            feedback.DoctorAction,
            feedback.Id);
    }

    public async Task<int> CountUnprocessedModifiedAsync(string modelName, CancellationToken ct = default)
    {
        return modelName switch
        {
            ModelTrainingNames.CheXNet => await _db.CheXNetModifiedData.AsNoTracking()
                .CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false),
            ModelTrainingNames.LungCancer => await _db.LungCancerModifiedData.AsNoTracking()
                .CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false),
            ModelTrainingNames.BioBERT => await _db.BioBERTModifiedData.AsNoTracking()
                .CountAsync(x => !x.IsProcessed, ct).ConfigureAwait(false),
            _ => 0
        };
    }

    private async Task UpsertAcceptedAsync(
        string modelName,
        PredictionFeedback feedback,
        string inputRef,
        string? sourceDataJson,
        string prediction,
        double? confidence,
        int? patientId,
        CancellationToken ct)
    {
        var existing = await FindAcceptedByFeedbackIdAsync(modelName, feedback.Id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.InputDataReference = inputRef;
            existing.SourceDataJson = sourceDataJson;
            existing.Prediction = prediction;
            existing.ConfidenceScore = confidence;
            existing.DoctorId = feedback.SubmittingDoctorUserId;
            existing.PatientId = patientId;
            existing.DicomStudyUid = feedback.StudyInstanceUid;
            existing.CreatedAt = feedback.CreatedAt;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var row = CreateAcceptedEntity(modelName);
        row.Id = Guid.NewGuid();
        row.ModelName = modelName;
        row.InputDataReference = inputRef;
        row.SourceDataJson = sourceDataJson;
        row.Prediction = prediction;
        row.ConfidenceScore = confidence;
        row.DoctorId = feedback.SubmittingDoctorUserId;
        row.PatientId = patientId;
        row.DicomStudyUid = feedback.StudyInstanceUid;
        row.CreatedAt = feedback.CreatedAt;
        row.SourceFeedbackId = feedback.Id;

        AddAccepted(modelName, row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertModifiedAsync(
        string modelName,
        PredictionFeedback feedback,
        string? sourceDataJson,
        string originalPrediction,
        string correctedPrediction,
        double? originalConfidence,
        int? patientId,
        CancellationToken ct)
    {
        var existing = await FindModifiedByFeedbackIdAsync(modelName, feedback.Id, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.SourceDataJson = sourceDataJson;
            existing.OriginalPrediction = originalPrediction;
            existing.CorrectedPrediction = correctedPrediction;
            existing.OriginalConfidence = originalConfidence;
            existing.DoctorNotes = feedback.ClinicalNotes;
            existing.DoctorId = feedback.SubmittingDoctorUserId;
            existing.PatientId = patientId;
            existing.DicomStudyUid = feedback.StudyInstanceUid;
            existing.CreatedAt = feedback.CreatedAt;
            existing.IsProcessed = false;
            existing.ProcessedAt = null;
            existing.TrainingBatchId = null;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var row = CreateModifiedEntity(modelName);
        row.Id = Guid.NewGuid();
        row.ModelName = modelName;
        row.SourceDataJson = sourceDataJson;
        row.OriginalPrediction = originalPrediction;
        row.CorrectedPrediction = correctedPrediction;
        row.OriginalConfidence = originalConfidence;
        row.DoctorNotes = feedback.ClinicalNotes;
        row.DoctorId = feedback.SubmittingDoctorUserId;
        row.PatientId = patientId;
        row.DicomStudyUid = feedback.StudyInstanceUid;
        row.CreatedAt = feedback.CreatedAt;
        row.SourceFeedbackId = feedback.Id;

        AddModified(modelName, row);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string BuildInputReference(PredictionFeedback feedback)
    {
        if (!string.IsNullOrWhiteSpace(feedback.TrainingAssetPointerJson))
            return feedback.TrainingAssetPointerJson.Trim();
        if (feedback.RelatedJobId is not null)
            return $"job:{feedback.RelatedJobId}";
        if (!string.IsNullOrWhiteSpace(feedback.StudyInstanceUid))
            return $"study:{feedback.StudyInstanceUid}";
        return $"session:{feedback.ClientSessionCorrelationId}";
    }

    private static string? BuildSourceDataJson(PredictionFeedback feedback, int? patientId)
    {
        var source = TrainingSourceData.FromJson(feedback.TrainingAssetPointerJson) ?? new TrainingSourceData
        {
            ModelKey = ModelTrainingNames.NormalizeModelKey(feedback.ModelKey) ?? feedback.ModelKey
        };

        source.RelatedJobId ??= feedback.RelatedJobId;
        source.StudyInstanceUid ??= feedback.StudyInstanceUid;
        source.SeriesInstanceUid ??= feedback.SeriesInstanceUid;
        source.PatientId ??= patientId;

        if (feedback.DoctorAction == PredictionFeedbackStatuses.DoctorModify)
        {
            source.CorrectedLabel = !string.IsNullOrWhiteSpace(feedback.CorrectedPrimaryLabel)
                ? feedback.CorrectedPrimaryLabel
                : source.CorrectedLabel;
        }

        if (string.IsNullOrWhiteSpace(source.ImagePath)
            && string.IsNullOrWhiteSpace(source.DicomPath)
            && string.IsNullOrWhiteSpace(source.OriginalClinicalText))
        {
            return feedback.TrainingAssetPointerJson?.Trim();
        }

        return source.ToJson();
    }

    private async Task<int?> ResolvePatientIdAsync(Guid? relatedJobId, CancellationToken ct)
    {
        if (relatedJobId is null)
            return null;

        var scanId = await _db.ChestAiBackgroundJobs.AsNoTracking()
            .Where(j => j.Id == relatedJobId)
            .Select(j => j.PatientScanId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (scanId is null)
            return null;

        return await _db.PatientScans.AsNoTracking()
            .Where(s => s.Id == scanId)
            .Select(s => (int?)s.PatientId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    internal static (string Prediction, double? Confidence) ExtractOriginalPrediction(string modelKey, string originalJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(originalJson);
            var root = doc.RootElement;

            if (modelKey.Equals("LungAI", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("predictedClass", out var pc) || root.TryGetProperty("predicted_class", out pc))
                    return (pc.GetString() ?? "", TryGetConfidence(root, "probabilities", pc.GetString()));
            }

            if (modelKey.Equals("CheXNet", StringComparison.OrdinalIgnoreCase))
            {
                if (root.TryGetProperty("topClass", out var tc) && tc.ValueKind == JsonValueKind.String)
                    return (tc.GetString() ?? "", root.TryGetProperty("topProb", out var tp) ? tp.GetDouble() : null);

                if (root.TryGetProperty("probabilities", out var probs) && probs.ValueKind == JsonValueKind.Object)
                {
                    var best = probs.EnumerateObject().OrderByDescending(p => p.Value.GetDouble()).FirstOrDefault();
                    return (best.Name ?? "", best.Value.ValueKind == JsonValueKind.Number ? best.Value.GetDouble() : null);
                }
            }

            if (modelKey.Equals("BioBERT", StringComparison.OrdinalIgnoreCase)
                && root.TryGetProperty("entities", out var ents)
                && ents.ValueKind == JsonValueKind.Array
                && ents.GetArrayLength() > 0)
            {
                var first = ents[0];
                var word = first.TryGetProperty("word", out var w) ? w.GetString() ?? "" : "";
                var score = first.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                    ? s.GetDouble()
                    : (double?)null;
                return (word, score);
            }
        }
        catch
        {
            /* ignore malformed snapshot */
        }

        return ("", null);
    }

    private static double? TryGetConfidence(JsonElement root, string propName, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        if (!root.TryGetProperty(propName, out var probs) || probs.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var p in probs.EnumerateObject())
        {
            if (p.Name.Equals(label, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number)
                return p.Value.GetDouble();
        }

        return null;
    }

    private async Task<ModelAcceptedDataBase?> FindAcceptedByFeedbackIdAsync(string modelName, Guid feedbackId, CancellationToken ct)
    {
        return modelName switch
        {
            ModelTrainingNames.CheXNet => await _db.CheXNetAcceptedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            ModelTrainingNames.LungCancer => await _db.LungCancerAcceptedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            ModelTrainingNames.BioBERT => await _db.BioBERTAcceptedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<ModelModifiedDataBase?> FindModifiedByFeedbackIdAsync(string modelName, Guid feedbackId, CancellationToken ct)
    {
        return modelName switch
        {
            ModelTrainingNames.CheXNet => await _db.CheXNetModifiedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            ModelTrainingNames.LungCancer => await _db.LungCancerModifiedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            ModelTrainingNames.BioBERT => await _db.BioBERTModifiedData
                .FirstOrDefaultAsync(x => x.SourceFeedbackId == feedbackId, ct).ConfigureAwait(false),
            _ => null
        };
    }

    private static ModelAcceptedDataBase CreateAcceptedEntity(string modelName) => modelName switch
    {
        ModelTrainingNames.CheXNet => new CheXNetAcceptedData(),
        ModelTrainingNames.LungCancer => new LungCancerAcceptedData(),
        ModelTrainingNames.BioBERT => new BioBERTAcceptedData(),
        _ => throw new ArgumentOutOfRangeException(nameof(modelName))
    };

    private static ModelModifiedDataBase CreateModifiedEntity(string modelName) => modelName switch
    {
        ModelTrainingNames.CheXNet => new CheXNetModifiedData(),
        ModelTrainingNames.LungCancer => new LungCancerModifiedData(),
        ModelTrainingNames.BioBERT => new BioBERTModifiedData(),
        _ => throw new ArgumentOutOfRangeException(nameof(modelName))
    };

    private void AddAccepted(string modelName, ModelAcceptedDataBase row)
    {
        switch (modelName)
        {
            case ModelTrainingNames.CheXNet:
                _db.CheXNetAcceptedData.Add((CheXNetAcceptedData)row);
                break;
            case ModelTrainingNames.LungCancer:
                _db.LungCancerAcceptedData.Add((LungCancerAcceptedData)row);
                break;
            case ModelTrainingNames.BioBERT:
                _db.BioBERTAcceptedData.Add((BioBERTAcceptedData)row);
                break;
        }
    }

    private void AddModified(string modelName, ModelModifiedDataBase row)
    {
        switch (modelName)
        {
            case ModelTrainingNames.CheXNet:
                _db.CheXNetModifiedData.Add((CheXNetModifiedData)row);
                break;
            case ModelTrainingNames.LungCancer:
                _db.LungCancerModifiedData.Add((LungCancerModifiedData)row);
                break;
            case ModelTrainingNames.BioBERT:
                _db.BioBERTModifiedData.Add((BioBERTModifiedData)row);
                break;
        }
    }
}
