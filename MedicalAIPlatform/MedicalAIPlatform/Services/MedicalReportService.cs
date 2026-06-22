using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

public sealed class MedicalReportService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MedicalReportService> _logger;

    public MedicalReportService(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<MedicalReportService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Guid?> GetLatestReportIdAsync(int patientId, int? patientScanId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ClinicalMedicalReports.AsNoTracking().Where(r => r.PatientId == patientId);

        if (patientScanId is int scanId)
        {
            query = query.Where(r =>
                r.ScanAiAnalysisId != null
                && _db.ScanAiAnalyses.Any(a => a.Id == r.ScanAiAnalysisId && a.PatientScanId == scanId));
        }

        return await query
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Compact JSON for assistant explanation (no large image payloads).</summary>
    public async Task<string?> GetLatestReportContextJsonAsync(int patientId,
        CancellationToken cancellationToken = default)
    {
        var id = await GetLatestReportIdAsync(patientId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (id is null)
            return null;

        var dto = await GetReportDtoAsync(id.Value, cancellationToken).ConfigureAwait(false);
        if (dto is null)
            return null;

        dto.OptionalImages = [];
        return JsonSerializer.Serialize(dto, JsonOpts);
    }

    public async Task<MedicalReportResponseDto?> GetReportDtoAsync(Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ClinicalMedicalReports.AsNoTracking()
            .Include(r => r.Patient)
            .Include(r => r.ScanAiAnalysis!)
                .ThenInclude(a => a.PatientScan)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : MapToDto(row, ParseSnapshot(row.AiSnapshotJson));
    }

    public async Task<Guid> GenerateAndPersistAsync(int patientId, string generatedByUserId,
        int? patientScanId = null, CancellationToken cancellationToken = default)
    {
        var scan = await ResolveScanForReportAsync(patientId, patientScanId, cancellationToken)
            .ConfigureAwait(false);

        if (scan.AiAnalysis?.MedicalReport is { } existingReport)
            return existingReport.Id;

        var analysis = scan.AiAnalysis
            ?? throw new InvalidOperationException(
                $"Scan {scan.Id} has no AI analysis row. Upload the study again or run model inference first.");

        var patient = await _db.Patients.AsNoTracking()
            .Include(p => p.HistoryEntries)
            .FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken)
            .ConfigureAwait(false);

        if (patient is null)
            throw new InvalidOperationException($"Patient {patientId} was not found.");

        var histories = patient.HistoryEntries.OrderByDescending(h => h.VisitDate).Take(24).ToList();
        // Reports MUST use persisted ScanAiAnalysis only — never IUserAnalyticsSessionStore (per-user in-memory workspace).
        var snapshot = BuildAiSnapshot(patient, histories, [scan], AnalyticsSessionSnapshot.Empty);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts);

        var report = new ClinicalMedicalReport
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ScanAiAnalysisId = analysis.Id,
            GeneratedByUserId = generatedByUserId,
            GeneratedAt = DateTimeOffset.UtcNow,
            AiSnapshotJson = snapshotJson,
            FindingsDisplay = snapshot.Findings,
            ImpressionDisplay = snapshot.Impression,
            RecommendationsDisplay = snapshot.Recommendations,
            ConfidenceSnapshot = snapshot.Confidence,
            ModelVersion = snapshot.ModelVersion,
            IsDoctorModified = false,
            DoctorModifiedAt = null,
            DoctorModifiedByUserId = null
        };

        report.Revisions.Add(new MedicalReportRevision
        {
            Source = "ai_generated",
            Findings = snapshot.Findings,
            Impression = snapshot.Impression,
            Recommendations = snapshot.Recommendations,
            CreatedAt = DateTimeOffset.UtcNow,
            ActorUserId = null
        });

        _db.ClinicalMedicalReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Generated clinical medical report {ReportId} for patient {PatientId} scan {ScanId} analysis {AnalysisId}",
            report.Id, patientId, scan.Id, analysis.Id);

        return report.Id;
    }

    private async Task<PatientScan> ResolveScanForReportAsync(int patientId, int? patientScanId,
        CancellationToken cancellationToken)
    {
        if (patientScanId is int scanId)
        {
            var explicitScan = await _db.PatientScans
                .Include(s => s.AiAnalysis!)
                    .ThenInclude(a => a.MedicalReport)
                .FirstOrDefaultAsync(s => s.Id == scanId && s.PatientId == patientId, cancellationToken)
                .ConfigureAwait(false);

            if (explicitScan is null)
                throw new InvalidOperationException(
                    $"Scan {scanId} was not found for patient {patientId}.");

            return explicitScan;
        }

        var scan = await _db.PatientScans
            .Include(s => s.AiAnalysis!)
                .ThenInclude(a => a.MedicalReport)
            .Where(s => s.PatientId == patientId)
            .OrderByDescending(s => s.ScanDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (scan is null)
            throw new InvalidOperationException(
                $"Patient {patientId} has no imaging studies. Upload a scan before generating a report.");

        return scan;
    }

    public async Task<bool> ApplyDoctorEditAsync(Guid reportId, string doctorUserId,
        MedicalReportClinicalEditDto edit, CancellationToken cancellationToken = default)
    {
        var report = await _db.ClinicalMedicalReports
            .Include(r => r.Revisions)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            .ConfigureAwait(false);

        if (report is null)
            return false;

        report.FindingsDisplay = edit.Findings ?? "";
        report.ImpressionDisplay = edit.Impression ?? "";
        report.RecommendationsDisplay = edit.Recommendations ?? "";
        report.IsDoctorModified = true;
        report.DoctorModifiedAt = DateTimeOffset.UtcNow;
        report.DoctorModifiedByUserId = doctorUserId;

        report.Revisions.Add(new MedicalReportRevision
        {
            Source = "doctor_edit",
            Findings = report.FindingsDisplay,
            Impression = report.ImpressionDisplay,
            Recommendations = report.RecommendationsDisplay,
            CreatedAt = DateTimeOffset.UtcNow,
            ActorUserId = doctorUserId
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> RevertToAiAsync(Guid reportId, string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var report = await _db.ClinicalMedicalReports
            .Include(r => r.Revisions)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            .ConfigureAwait(false);

        if (report is null)
            return false;

        var snap = ParseSnapshot(report.AiSnapshotJson);
        report.FindingsDisplay = snap.Findings;
        report.ImpressionDisplay = snap.Impression;
        report.RecommendationsDisplay = snap.Recommendations;
        report.ConfidenceSnapshot = snap.Confidence;
        report.IsDoctorModified = false;
        report.DoctorModifiedAt = null;
        report.DoctorModifiedByUserId = null;

        report.Revisions.Add(new MedicalReportRevision
        {
            Source = "revert_to_ai",
            Findings = report.FindingsDisplay,
            Impression = report.ImpressionDisplay,
            Recommendations = report.RecommendationsDisplay,
            CreatedAt = DateTimeOffset.UtcNow,
            ActorUserId = actorUserId
        });

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private MedicalReportAiSnapshot BuildAiSnapshot(Patient patient, List<PatientHistory> histories,
        List<PatientScan> scans, AnalyticsSessionSnapshot sessionSnapshot)
    {
        var modelVersion = _configuration["MedicalReport:ModelVersion"] ?? "chestai-report-v1";
        var demographics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["full_name"] = patient.FullName,
            ["date_of_birth"] = patient.DateOfBirth.ToString("yyyy-MM-dd"),
            ["gender"] = patient.Gender,
            ["patient_record_number"] = patient.PatientId ?? "",
            ["phone"] = patient.PhoneNumber ?? "",
            ["email"] = patient.Email ?? ""
        };

        var summaryLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(patient.MedicalHistorySummary))
            summaryLines.Add(patient.MedicalHistorySummary.Trim());
        if (!string.IsNullOrWhiteSpace(patient.Allergies))
            summaryLines.Add($"Allergies: {patient.Allergies.Trim()}");
        if (!string.IsNullOrWhiteSpace(patient.CurrentMedications))
            summaryLines.Add($"Medications: {patient.CurrentMedications.Trim()}");

        var visits = histories.Select(h => new MedicalReportVisitDto
        {
            VisitDate = h.VisitDate.ToUniversalTime().ToString("yyyy-MM-dd"),
            VisitType = h.VisitType,
            ChiefComplaint = h.ChiefComplaint,
            Diagnosis = h.Diagnosis,
            ClinicalNotes = Truncate(h.ClinicalNotes ?? h.ConditionDescription, 1200)
        }).ToList();

        var composed = MedicalReportInferenceComposer.Compose(scans, sessionSnapshot);

        var findingsChart = BuildFindingsFromChart(histories);
        var sessionAppendix = BuildSessionContextAppendix(sessionSnapshot);

        var findingsParts = new List<string> { composed.ImagingFindingsBlock };
        if (!string.IsNullOrWhiteSpace(findingsChart))
        {
            findingsParts.Add("=== Chart context ===");
            findingsParts.Add(findingsChart);
        }

        if (!string.IsNullOrWhiteSpace(sessionAppendix))
            findingsParts.Add(sessionAppendix);

        var findings = string.Join("\n\n", findingsParts.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(findings))
            findings =
                "No quantitative imaging outputs or chart rows were available to summarize at generation time.";

        var optionalImages = MergeImagesDedupe(composed.StudyImages, CollectOptionalImages(sessionSnapshot));

        return new MedicalReportAiSnapshot
        {
            MedicalHistory = new MedicalReportMedicalHistoryDto
            {
                Demographics = demographics,
                SummaryLines = summaryLines,
                Visits = visits
            },
            AiAnalysis = composed.AiAnalysis,
            Findings = findings,
            Impression = composed.Impression.Trim(),
            Recommendations = composed.Recommendations.Trim(),
            Confidence = composed.Confidence,
            ConfidenceMethod = composed.ConfidenceMethod,
            GeneratedAt = DateTimeOffset.UtcNow,
            ModelVersion = modelVersion,
            OptionalImages = optionalImages
        };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string BuildSessionContextAppendix(AnalyticsSessionSnapshot session)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(session.ClinicalText))
        {
            sb.AppendLine("=== Supplemental clinical text (analytics workspace) ===");
            sb.AppendLine(Truncate(session.ClinicalText, 1500));
        }

        if (session.PipelineNotes.Count > 0)
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.AppendLine("Pipeline notes:");
            foreach (var n in session.PipelineNotes)
                sb.AppendLine($"• {n}");
        }

        return sb.ToString().Trim();
    }

    private static List<MedicalReportImageRefDto> MergeImagesDedupe(
        IEnumerable<MedicalReportImageRefDto> primary,
        IEnumerable<MedicalReportImageRefDto> secondary)
    {
        var list = new List<MedicalReportImageRefDto>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var img in primary.Concat(secondary))
        {
            var key = img.DataUrl ?? "";
            if (key.Length == 0 || !seen.Add(key))
                continue;
            list.Add(img);
        }

        return list;
    }

    private static string BuildFindingsFromChart(List<PatientHistory> histories)
    {
        if (histories.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("Recent encounters (structured from patient chart):");
        foreach (var h in histories.Take(8))
        {
            sb.AppendLine(
                $"• {h.VisitDate:yyyy-MM-dd} ({h.VisitType ?? "Visit"}): " +
                $"{Truncate(h.ChiefComplaint ?? h.ConditionDescription ?? h.Diagnosis ?? h.ClinicalNotes, 400)}");
        }

        return sb.ToString().Trim();
    }

    private static List<MedicalReportImageRefDto> CollectOptionalImages(AnalyticsSessionSnapshot session)
    {
        var list = new List<MedicalReportImageRefDto>();

        if (session.CurrentResults is { Count: > 0 })
        {
            foreach (var kv in session.CurrentResults)
            {
                var hm = kv.Value.Heatmap;
                if (!string.IsNullOrWhiteSpace(hm.DataUrl))
                    list.Add(new MedicalReportImageRefDto
                        { Label = $"CheXNet heatmap ({kv.Key}: {hm.ClassName})", DataUrl = hm.DataUrl });

                if (!string.IsNullOrWhiteSpace(kv.Value.PreviewImageDataUrl))
                    list.Add(new MedicalReportImageRefDto
                        { Label = $"Imaging preview ({kv.Key})", DataUrl = kv.Value.PreviewImageDataUrl });
            }
        }

        if (!string.IsNullOrWhiteSpace(session.XRayImageDataUrl))
            list.Add(new MedicalReportImageRefDto { Label = "Session X-ray preview", DataUrl = session.XRayImageDataUrl! });

        if (!string.IsNullOrWhiteSpace(session.CTImageDataUrl))
            list.Add(new MedicalReportImageRefDto { Label = "Session CT preview", DataUrl = session.CTImageDataUrl! });

        if (session.CurrentLungAIResults is { } lung &&
            !string.IsNullOrWhiteSpace(lung.PreviewImageDataUrl))
            list.Add(new MedicalReportImageRefDto { Label = "LungAI CT preview", DataUrl = lung.PreviewImageDataUrl });

        return list;
    }

    private static MedicalReportAiSnapshot ParseSnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MedicalReportAiSnapshot>(json, JsonOpts)
                   ?? new MedicalReportAiSnapshot();
        }
        catch
        {
            return new MedicalReportAiSnapshot();
        }
    }

    private static MedicalReportResponseDto MapToDto(ClinicalMedicalReport row, MedicalReportAiSnapshot snap)
    {
        var aiSnap = snap.AiAnalysis ?? new MedicalReportAiAnalysisSnapshotDto();
        return new MedicalReportResponseDto
        {
            Id = row.Id,
            PatientId = row.PatientId,
            PatientScanId = row.ScanAiAnalysis?.PatientScanId,
            ScanAiAnalysisId = row.ScanAiAnalysisId,
            PatientDisplayName = row.Patient.FullName,
            StatusBadge = row.IsDoctorModified ? "doctor_modified" : "ai_generated",
            MedicalHistory = snap.MedicalHistory,
            Findings = row.FindingsDisplay,
            Impression = row.ImpressionDisplay,
            Recommendations = row.RecommendationsDisplay,
            Confidence = row.ConfidenceSnapshot,
            GeneratedAt = row.GeneratedAt,
            ModelVersion = row.ModelVersion,
            DoctorModifiedAt = row.DoctorModifiedAt,
            OptionalImages = snap.OptionalImages,
            AiAnalysis = MapAiAnalysis(aiSnap),
            ConfidenceMethod = FormatConfidenceMethod(snap.ConfidenceMethod),
            AiOriginal = new MedicalReportAiSectionsDto
            {
                Findings = snap.Findings,
                Impression = snap.Impression,
                Recommendations = snap.Recommendations
            }
        };
    }

    private static MedicalReportAiAnalysisDto MapAiAnalysis(MedicalReportAiAnalysisSnapshotDto s) =>
        new()
        {
            ChexnetProbabilities = s.ChexnetProbabilities
                .Select(p => new MedicalReportProbabilityRowDto
                    { Label = p.Label, Probability = p.Probability, Source = p.Source }).ToList(),
            CtProbabilities = s.CtProbabilities
                .Select(p => new MedicalReportProbabilityRowDto
                    { Label = p.Label, Probability = p.Probability, Source = p.Source }).ToList(),
            ClinicalEntities = s.ClinicalEntities
                .Select(e => new MedicalReportEntityRowDto { Text = e.Text, Group = e.Group, Score = e.Score })
                .ToList(),
            TopConditionLabel = s.TopConditionLabel,
            TopConditionProbability = s.TopConditionProbability,
            RiskTier = s.RiskTier,
            ConfidenceCaption = s.ConfidenceCaption,
            HeatmapNote = s.HeatmapNote,
            UsedPatientStudies = s.UsedPatientStudies,
            UsedLiveSession = s.UsedLiveSession
        };

    private static string FormatConfidenceMethod(string? slug) =>
        slug switch
        {
            "maximum_merged_chexnet_probability" => "Maximum merged CheXNet pathology probability",
            "maximum_merged_lungai_probability" => "Maximum merged LungAI (CT) class probability",
            "top_biobert_entity_score" => "Top BioBERT entity confidence (text auxiliary)",
            "no_model_scores_chart_context_only" => "Chart context only — no imaging model scores merged",
            null or "" => "Legacy estimate — regenerate report for model-grounded confidence",
            _ => slug.Replace('_', ' ')
        };
}
