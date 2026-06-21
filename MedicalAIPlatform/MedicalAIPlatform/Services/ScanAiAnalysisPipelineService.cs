using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

/// <summary>Creates and updates <see cref="ScanAiAnalysis"/> rows in the Patient → Scan → Analysis → Report pipeline.</summary>
public sealed class ScanAiAnalysisPipelineService
{
    private readonly ApplicationDbContext _db;

    public ScanAiAnalysisPipelineService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ScanAiAnalysis> GetOrCreateForScanAsync(int patientScanId, string? userId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.ScanAiAnalyses
            .FirstOrDefaultAsync(a => a.PatientScanId == patientScanId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
            return existing;

        var analysis = new ScanAiAnalysis
        {
            PatientScanId = patientScanId,
            Status = ScanAiAnalysisStatuses.Pending,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.ScanAiAnalyses.Add(analysis);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return analysis;
    }

    public async Task<ScanAiAnalysis> ApplyModelResultsAsync(
        ScanAiAnalysis analysis,
        string? chexnetJson,
        string? biobertJson,
        string? lungaiJson,
        IReadOnlyList<string> linkedModels,
        string generatedSummary,
        CancellationToken cancellationToken = default)
    {
        analysis.LinkedModels = linkedModels.Count > 0 ? string.Join(",", linkedModels) : null;
        analysis.CheXNetResults = chexnetJson;
        analysis.BioBertResults = biobertJson;
        analysis.LungAIResults = lungaiJson;
        analysis.GeneratedResult = generatedSummary;
        analysis.ResultGeneratedAt = DateTime.UtcNow;
        analysis.Status = ScanAiAnalysisStatuses.Completed;
        analysis.CompletedAt = DateTime.UtcNow;
        analysis.ErrorMessage = null;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return analysis;
    }
}
