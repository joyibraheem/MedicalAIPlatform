using System.Text.Json;
using System.Text.Json.Serialization;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services;

/// <summary>Seeds synthetic DEMO-* patients with history, mock scans/AI JSON, and structured reports for UI/testing.</summary>
public sealed class MedicalPlatformDemoDataSeeder
{
    public const string MrnPrefix = "DEMO-MRN-";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ApplicationDbContext _db;
    private readonly DemoDataSeederOptions _opt;
    private readonly ILogger<MedicalPlatformDemoDataSeeder> _logger;

    public MedicalPlatformDemoDataSeeder(
        ApplicationDbContext db,
        IOptions<DemoDataSeederOptions> options,
        ILogger<MedicalPlatformDemoDataSeeder> logger)
    {
        _db = db;
        _opt = options.Value;
        _logger = logger;
    }

    public async Task<DemoSeedResultDto> SeedAsync(bool force, string actorUserId, CancellationToken cancellationToken)
    {
        var result = new DemoSeedResultDto();

        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            result.Message = "Actor user id is required.";
            return result;
        }

        var exists = await _db.Patients.AsNoTracking()
            .AnyAsync(p => p.PatientId != null && p.PatientId.StartsWith(MrnPrefix), cancellationToken)
            .ConfigureAwait(false);

        if (exists && !force)
        {
            result.Skipped = true;
            result.Message =
                $"Demo data already present ({MrnPrefix}*). POST with force=true to replace (removes demo patients only).";
            return result;
        }

        if (force && exists)
            await RemoveDemoPatientsAsync(cancellationToken).ConfigureAwait(false);

        var rng = new Random(_opt.RandomSeed);
        var scenarios = BuildScenarios(rng);

        foreach (var s in scenarios)
        {
            var dob = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-s.Age).AddDays(rng.Next(-120, 120)).ToDateTime(TimeOnly.MinValue);

            var patient = new Patient
            {
                FirstName = s.FirstName,
                LastName = s.LastName,
                DateOfBirth = dob,
                Gender = s.Gender,
                PatientId = $"{MrnPrefix}{s.MrnIndex:D3}",
                PhoneNumber = s.Phone,
                Email = s.Email,
                Address = s.Address,
                MedicalHistorySummary = Truncate(s.ChartSummary, 500),
                Allergies = string.IsNullOrEmpty(s.Allergies) ? null : Truncate(s.Allergies, 500),
                CurrentMedications = string.IsNullOrEmpty(s.Medications) ? null : Truncate(s.Medications, 500),
                ProfileImagePath = null,
                CreatedByUserId = actorUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var h in s.HistoryRows)
            {
                var vd = DateTime.UtcNow.AddDays(-h.DaysAgo).AddHours(-rng.Next(1, 18));
                patient.HistoryEntries.Add(new PatientHistory
                {
                    VisitDate = vd,
                    VisitType = h.VisitType,
                    ChiefComplaint = Truncate(h.ChiefComplaint, 1000),
                    ConditionDescription = Truncate(h.ConditionDescription, 5000),
                    ClinicalNotes = Truncate(h.ClinicalNotes, 5000),
                    Diagnosis = Truncate(h.Diagnosis, 5000),
                    TreatmentPlan = Truncate(h.TreatmentPlan, 5000),
                    CreatedByUserId = actorUserId,
                    CreatedAt = vd
                });
            }

            foreach (var sc in s.ScanRows)
            {
                var scanDate = DateTime.UtcNow.AddDays(-sc.DaysAgo);
                patient.Scans.Add(new PatientScan
                {
                    ScanType = sc.ScanType,
                    ScanDate = scanDate,
                    FileName = sc.FileName,
                    ContentType = sc.ContentType,
                    ImagePath = sc.ImagePath,
                    ImageData = null,
                    ImageDataUrl = null,
                    CreatedByUserId = actorUserId,
                    CreatedAt = scanDate,
                    AiAnalysis = new ScanAiAnalysis
                    {
                        Status = ScanAiAnalysisStatuses.Completed,
                        CheXNetResults = sc.CheXNetJson,
                        BioBertResults = sc.BioBertJson,
                        LungAIResults = sc.LungAiJson,
                        LinkedModels = sc.LinkedModels,
                        GeneratedResult = Truncate(sc.GeneratedSummary, 5000),
                        ResultGeneratedAt = scanDate.AddMinutes(15),
                        CompletedAt = scanDate.AddMinutes(15),
                        CreatedByUserId = actorUserId,
                        CreatedAt = scanDate
                    }
                });
            }

            _db.Patients.Add(patient);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            result.PatientsCreated++;
            result.HistoryEntriesCreated += s.HistoryRows.Count;
            result.ScansCreated += s.ScanRows.Count;

            var snapshot = BuildSnapshot(patient, s);
            var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts);

            var findings = s.Report.Findings;
            var impression = s.Report.Impression;
            var recommendations = s.Report.Recommendations;

            if (s.ApplyDoctorEdits)
            {
                findings += "\n\n[Physician chart clarification appended during QA demo: correlate with prior outpatient spirometry.]";
            }

            var report = new ClinicalMedicalReport
            {
                Id = Guid.NewGuid(),
                PatientId = patient.Id,
                ScanAiAnalysisId = patient.Scans
                    .OrderByDescending(sc => sc.ScanDate)
                    .Select(sc => sc.AiAnalysis?.Id)
                    .FirstOrDefault(id => id.HasValue),
                GeneratedByUserId = actorUserId,
                GeneratedAt = DateTimeOffset.UtcNow.AddDays(-rng.Next(1, 14)),
                AiSnapshotJson = snapshotJson,
                FindingsDisplay = findings,
                ImpressionDisplay = impression,
                RecommendationsDisplay = recommendations,
                ConfidenceSnapshot = s.Report.Confidence,
                ModelVersion = _opt.ReportModelVersion,
                IsDoctorModified = s.ApplyDoctorEdits,
                DoctorModifiedAt = s.ApplyDoctorEdits ? DateTimeOffset.UtcNow.AddDays(-rng.Next(1, 5)) : null,
                DoctorModifiedByUserId = s.ApplyDoctorEdits ? actorUserId : null
            };

            report.Revisions.Add(new MedicalReportRevision
            {
                Source = "ai_generated",
                Findings = snapshot.Findings,
                Impression = snapshot.Impression,
                Recommendations = snapshot.Recommendations,
                CreatedAt = report.GeneratedAt,
                ActorUserId = null
            });

            if (s.ApplyDoctorEdits)
            {
                report.Revisions.Add(new MedicalReportRevision
                {
                    Source = "doctor_edit",
                    Findings = findings,
                    Impression = impression,
                    Recommendations = recommendations,
                    CreatedAt = report.DoctorModifiedAt ?? DateTimeOffset.UtcNow,
                    ActorUserId = actorUserId
                });
            }

            _db.ClinicalMedicalReports.Add(report);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            result.ReportsCreated++;
        }

        result.Message = $"Seeded {result.PatientsCreated} demo patients with histories, scans, and reports.";
        _logger.LogInformation("Demo data seeded: patients={P}, scans={S}, reports={R}", result.PatientsCreated,
            result.ScansCreated, result.ReportsCreated);

        return result;
    }

    private async Task RemoveDemoPatientsAsync(CancellationToken cancellationToken)
    {
        var ids = await _db.Patients.Where(p => p.PatientId != null && p.PatientId.StartsWith(MrnPrefix))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (ids.Count == 0)
            return;

        var scans = await _db.PatientScans.Where(s => ids.Contains(s.PatientId)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.PatientScans.RemoveRange(scans);

        var reports = await _db.ClinicalMedicalReports.Where(r => ids.Contains(r.PatientId))
            .Include(r => r.Revisions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.ClinicalMedicalReports.RemoveRange(reports);

        var patients = await _db.Patients.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        _db.Patients.RemoveRange(patients);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Removed {Count} demo patients and dependents (force reseed).", patients.Count);
    }

    public Task<bool> DemoPatientsExistAsync(CancellationToken cancellationToken = default) =>
        _db.Patients.AsNoTracking()
            .AnyAsync(p => p.PatientId != null && p.PatientId.StartsWith(MrnPrefix), cancellationToken);

    private MedicalReportAiSnapshot BuildSnapshot(Patient patient, Scenario s)
    {
        var demographics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["full_name"] = patient.FullName,
            ["date_of_birth"] = patient.DateOfBirth.ToString("yyyy-MM-dd"),
            ["gender"] = patient.Gender,
            ["patient_record_number"] = patient.PatientId ?? "",
            ["phone"] = patient.PhoneNumber ?? "",
            ["email"] = patient.Email ?? "",
            ["smoking_status"] = s.SmokingStatus,
            ["severity_screening_tag"] = s.SeverityTag
        };

        var summaryLines = new List<string>
        {
            patient.MedicalHistorySummary ?? "",
            $"Allergies: {patient.Allergies ?? "NKDA"}",
            $"Medications: {patient.CurrentMedications ?? "None documented"}"
        };

        var visits = patient.HistoryEntries.OrderByDescending(h => h.VisitDate).Select(h => new MedicalReportVisitDto
        {
            VisitDate = h.VisitDate.ToUniversalTime().ToString("yyyy-MM-dd"),
            VisitType = h.VisitType,
            ChiefComplaint = h.ChiefComplaint,
            Diagnosis = h.Diagnosis,
            ClinicalNotes = Truncate(h.ClinicalNotes ?? h.ConditionDescription, 900)
        }).ToList();

        return new MedicalReportAiSnapshot
        {
            MedicalHistory = new MedicalReportMedicalHistoryDto
            {
                Demographics = demographics,
                SummaryLines = summaryLines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                Visits = visits
            },
            Findings = s.Report.Findings,
            Impression = s.Report.Impression,
            Recommendations = s.Report.Recommendations,
            Confidence = s.Report.Confidence,
            GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ModelVersion = _opt.ReportModelVersion,
            OptionalImages =
            [
                new MedicalReportImageRefDto
                {
                    Label = "Placeholder preview (no pixel data)",
                    DataUrl = "data:image/svg+xml," + Uri.EscapeDataString(
                        "<svg xmlns='http://www.w3.org/2000/svg' width='120' height='80'><rect fill='#e2e8f0' width='120' height='80'/><text x='12' y='44' font-size='10' fill='#475569'>DEMO</text></svg>")
                }
            ]
        };
    }

    private static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var t = text.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static CheXNetPredictionResponse ChexNetFrom(Dictionary<string, double> probs)
    {
        var sorted = probs.OrderByDescending(kv => kv.Value).ToList();
        var top = sorted.Take(6).Select(kv => new CheXNetTopKItem { ClassName = kv.Key, Probability = kv.Value })
            .ToList();
        var pneumonia = probs.TryGetValue("Pneumonia", out var p)
            ? p
            : probs.TryGetValue("Consolidation", out var c)
                ? c
                : sorted.FirstOrDefault().Value;

        return new CheXNetPredictionResponse
        {
            ClassNames = probs.Keys.ToArray(),
            Probabilities = probs,
            TopK = top,
            Heatmap = new CheXNetHeatmap { ClassName = top.FirstOrDefault()?.ClassName ?? "", Mime = "image/png", ImageBase64 = "" },
            PneumoniaProbability = pneumonia,
            Device = "demo-seed",
            InferenceMs = 37 + probs.Count
        };
    }

    private static string SerializeBio(params (string Word, string Group, double Score)[] rows)
    {
        var entities = rows.Select((t, i) => new BioBertEntity
            { Word = t.Word, EntityGroup = t.Group, Score = t.Score, Start = i * 4, End = i * 4 + t.Word.Length }).ToList();
        return JsonSerializer.Serialize(new BioBertResponse { Entities = entities }, JsonOpts);
    }

    private sealed record HistoryRow(
        int DaysAgo,
        string VisitType,
        string ChiefComplaint,
        string ConditionDescription,
        string ClinicalNotes,
        string Diagnosis,
        string TreatmentPlan);

    private sealed record ScanRow(
        int DaysAgo,
        string ScanType,
        string FileName,
        string ImagePath,
        string ContentType,
        string LinkedModels,
        string? CheXNetJson,
        string? BioBertJson,
        string? LungAiJson,
        string GeneratedSummary);

    private sealed record ReportBody(string Findings, string Impression, string Recommendations, double Confidence);

    private sealed class Scenario
    {
        public required string FirstName { get; init; }
        public required string LastName { get; init; }
        public required int Age { get; init; }
        public required string Gender { get; init; }
        public required int MrnIndex { get; init; }
        public required string Phone { get; init; }
        public required string Email { get; init; }
        public required string Address { get; init; }
        public required string ChartSummary { get; init; }
        public string Allergies { get; init; } = "";
        public string Medications { get; init; } = "";
        public required string SmokingStatus { get; init; }
        public required string SeverityTag { get; init; }
        public required IReadOnlyList<HistoryRow> HistoryRows { get; init; }
        public required IReadOnlyList<ScanRow> ScanRows { get; init; }
        public required ReportBody Report { get; init; }
        public bool ApplyDoctorEdits { get; init; }
    }

    private static Scenario Mk1(Random rng, string fn, string ln, int age, string gender, int mrn, string cityAddr,
        string summary, string allergies, string meds, string smoking, string severity,
        HistoryRow h1, ScanRow scan1, ReportBody rep, bool doctorEdit) =>
        new()
        {
            FirstName = fn,
            LastName = ln,
            Age = age,
            Gender = gender,
            MrnIndex = mrn,
            Phone = $"+1-555-{100 + mrn:D3}-{1000 + mrn:D4}",
            Email = $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}@demo-patient.example.org",
            Address = $"{rng.Next(100, 9999)} Maple Ave, {cityAddr}",
            ChartSummary = summary,
            Allergies = allergies,
            Medications = meds,
            SmokingStatus = smoking,
            SeverityTag = severity,
            HistoryRows = [h1],
            ScanRows = [scan1],
            Report = rep,
            ApplyDoctorEdits = doctorEdit
        };

    private static Scenario Mk2(Random rng, string fn, string ln, int age, string gender, int mrn, string cityAddr,
        string summary, string allergies, string meds, string smoking, string severity,
        HistoryRow h1, HistoryRow h2, ScanRow scan1, ReportBody rep, bool doctorEdit) =>
        new()
        {
            FirstName = fn,
            LastName = ln,
            Age = age,
            Gender = gender,
            MrnIndex = mrn,
            Phone = $"+1-555-{100 + mrn:D3}-{1000 + mrn:D4}",
            Email = $"{fn.ToLowerInvariant()}.{ln.ToLowerInvariant()}@demo-patient.example.org",
            Address = $"{rng.Next(100, 9999)} Maple Ave, {cityAddr}",
            ChartSummary = summary,
            Allergies = allergies,
            Medications = meds,
            SmokingStatus = smoking,
            SeverityTag = severity,
            HistoryRows = [h1, h2],
            ScanRows = [scan1],
            Report = rep,
            ApplyDoctorEdits = doctorEdit
        };

    private static List<Scenario> BuildScenarios(Random rng)
    {
        string J(object o) => JsonSerializer.Serialize(o, JsonOpts);

        HistoryRow H(int daysAgo, string visitType, string chief, string condition, string notes, string dx,
            string tx) =>
            new(daysAgo, visitType, chief, condition, notes, dx, tx);

        ScanRow S(int daysAgo, string scanType, string file, string path, string ct, string linked,
            CheXNetPredictionResponse chex, string bio, string? lungJson, string gen) =>
            new(daysAgo, scanType, file, path, ct, linked, J(chex), bio, lungJson, gen);

        ReportBody R(string f, string i, string r, double c) => new(f, i, r, c);

        var list = new List<Scenario>();

        list.Add(Mk2(rng, "Elena", "Martinez", 54, "Female", 1, "Seattle, WA",
            "Intermittent wheeze and cough; mild asthma follow-up. Former smoker 5 py, quit 2018. Exercise tolerance good.",
            "NKDA", "Albuterol PRN", "Former smoker (5 py)", "mild",
            H(45, "Follow-up", "Dry cough 3 weeks", "Stable asthma",
                "Exam: mild expiratory wheeze. SpO2 98% RA.", "Asthma, intermittent",
                "Continue ICS-LABA; reviewed inhaler technique."),
            H(14, "Telehealth", "Medication refill", "Seasonal allergy overlap",
                "Reports daytime symptoms <2x/week.", "Allergic rhinitis",
                "Cetirizine PRN."),
            S(40, "XRay", "chest_xray_1.png", "/demo-imaging/chest_xray_1.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Atelectasis"] = 0.18,
                    ["Consolidation"] = 0.12,
                    ["Pneumonia"] = 0.14,
                    ["Pleural Effusion"] = 0.09,
                    ["Mass/Nodule"] = 0.11,
                    ["Cardiomegaly"] = 0.22
                }),
                SerializeBio(("wheeze", "SYMPTOM", 0.88), ("asthma", "DX", 0.81), ("cough", "SYMPTOM", 0.76)),
                null,
                "CheXNet (demo): low-grade heterogeneous opacities without focal consolidation. Suitable for routine asthma surveillance."),
            R("Low suspicion acute cardiopulmonary process on simulated frontal chest radiograph.",
                "Findings compatible with chronic airway disease without convincing pneumonia.",
                "Continue outpatient asthma management; return precautions for fever or worsening dyspnea.",
                0.71),
            false));

        list.Add(Mk1(rng, "James", "Okonkwo", 67, "Male", 2, "Atlanta, GA",
            "COPD GOLD 2, chronic cough with increased sputum; moderate exacerbation risk.",
            "Penicillin rash (childhood)", "Tiotropium-olodaterol, albuterol MDI", "Current smoker 42 py", "moderate",
            H(10, "ED observation", "Shortness of breath", "Productive cough green sputum",
                "CXR ordered; mild fatigue.", "Acute bronchitis vs COPD exacerbation",
                "Oral steroids 5d course; augment bronchodilators."),
            S(10, "XRay", "chest_xray_2.png", "/demo-imaging/chest_xray_2.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.42,
                    ["Consolidation"] = 0.38,
                    ["Pleural Effusion"] = 0.31,
                    ["Atelectasis"] = 0.35,
                    ["Edema"] = 0.22,
                    ["Mass/Nodule"] = 0.15
                }),
                SerializeBio(("COPD", "DX", 0.92), ("dyspnea", "SYMPTOM", 0.84), ("effusion", "FINDING", 0.41)),
                null,
                "CheXNet (demo): bibasilar opacities with small pleural fluid suspicion — correlate clinically."),
            R("Bibasilar airspace changes with intermediate probability pleural effusion on simulated projection.",
                "Pattern suggestive of infectious or inflammatory process superimposed on obstructive lung disease.",
                "Repeat imaging if hypoxic; consider antibiotic therapy per institutional COPD pathway.",
                0.68),
            false));

        list.Add(Mk1(rng, "Priya", "Shah", 41, "Female", 3, "Chicago, IL",
            "Fever, pleuritic chest pain x4 days; moderate severity community-acquired pneumonia suspicion.",
            "NKDA", "Azithromycin (pending cultures)", "Never smoker", "moderate",
            H(5, "Urgent care", "Fever 101.2°F", "Right-sided chest pain with inspiration",
                "RR 22, SpO2 93% RA.", "CAP suspicion",
                "Empiric antibiotics started; follow-up 48h."),
            S(5, "XRay", "chest_xray_3.png", "/demo-imaging/chest_xray_3.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.82,
                    ["Consolidation"] = 0.76,
                    ["Pleural Effusion"] = 0.21,
                    ["Mass/Nodule"] = 0.65,
                    ["Atelectasis"] = 0.28,
                    ["Cardiomegaly"] = 0.14
                }),
                SerializeBio(("pneumonia", "DX", 0.79), ("consolidation", "FINDING", 0.71), ("effusion", "FINDING", 0.33)),
                null,
                "CheXNet (demo): focal consolidation right lower zone — aligns with infectious symptoms."),
            R("Right lower lobe–predominant consolidation with moderate pneumonia probability and adjacent pleural reaction unlikely.",
                "Imaging pattern compatible with community-acquired pneumonia in appropriate clinical context.",
                "Complete antibiotic course; urgent return for hypoxia or hemodynamic instability.",
                0.81),
            true));

        list.Add(Mk1(rng, "Robert", "Klein", 72, "Male", 4, "Phoenix, AZ",
            "Incidental 8 mm pulmonary nodule noted on prior imaging; active surveillance per Fleischner-style pathway.",
            "Sulfa allergy", "Rosuvastatin", "Former smoker 22 py", "mild",
            H(120, "Imaging review", "Nodule follow-up", "Stable cough unrelated",
                "Discussed risks/benefits of surveillance CT.", "Pulmonary nodule surveillance",
                "Annual low-dose CT scheduled."),
            S(118, "XRay", "chest_xray_4.png", "/demo-imaging/chest_xray_4.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mass/Nodule"] = 0.62,
                    ["Pleural Effusion"] = 0.08,
                    ["Pneumonia"] = 0.11,
                    ["Atelectasis"] = 0.19,
                    ["Cardiomegaly"] = 0.27,
                    ["Consolidation"] = 0.13
                }),
                SerializeBio(("nodule", "FINDING", 0.88)),
                null,
                "CheXNet (demo): subtle nodular opacity — correlate with dedicated CT cadence."),
            R("Small nodular opacity without ancillary acute findings on simulated radiograph.",
                "Low acute risk profile; prioritize longitudinal CT comparison rather than acute intervention.",
                "Maintain structured surveillance; tobacco cessation reinforcement.",
                0.74),
            false));

        list.Add(Mk1(rng, "Linda", "Chen", 58, "Female", 5, "San Diego, CA",
            "Progressive exertional dyspnea; history of hypertension; evaluate for cardiomegaly vs infiltrate.",
            "NKDA", "Lisinopril, HCTZ", "Never smoker", "moderate",
            H(20, "Cardiology co-manage", "Dyspnea on stairs", "Orthopnea denied",
                "JVP not elevated on tele snapshot.", "Dyspnea — multifactorial",
                "Optimize BP meds; obtain CXR."),
            S(20, "XRay", "chest_xray_5.png", "/demo-imaging/chest_xray_5.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cardiomegaly"] = 0.69,
                    ["Edema"] = 0.44,
                    ["Pleural Effusion"] = 0.26,
                    ["Pneumonia"] = 0.19,
                    ["Consolidation"] = 0.16,
                    ["Mass/Nodule"] = 0.12
                }),
                SerializeBio(("cardiomegaly", "FINDING", 0.77), ("dyspnea", "SYMPTOM", 0.72)),
                null,
                "CheXNet (demo): enlarged cardiac silhouette — clinical correlation for heart failure versus technique."),
            R("Cardiac silhouette enlargement with mild pulmonary vascular prominence on simulated study.",
                "Findings raise suspicion for cardiomegaly and early congestion; pneumonia less likely.",
                "Align with cardiology; consider BNP and echocardiography per pathway.",
                0.69),
            false));

        list.Add(Mk1(rng, "Amir", "Hassan", 49, "Male", 6, "Houston, TX",
            "Post-operative day 3 after laparoscopic procedure; fever spikes — rule out pneumonia.",
            "NKDA", "Enoxaparin prophylaxis", "Never smoker", "severe",
            H(3, "Inpatient", "Fever spike 102°F", "Tachypnea overnight",
                "CXR portable.", "Hospital-acquired pneumonia suspicion",
                "Blood cultures; escalate antibiotics per protocol."),
            S(3, "XRay", "chest_xray_6.png", "/demo-imaging/chest_xray_6.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.76,
                    ["Consolidation"] = 0.71,
                    ["Pleural Effusion"] = 0.34,
                    ["Atelectasis"] = 0.52,
                    ["Edema"] = 0.29,
                    ["Mass/Nodule"] = 0.17
                }),
                SerializeBio(("postoperative", "PROCEDURE", 0.61), ("pneumonia", "DX", 0.74)),
                null,
                "CheXNet (demo): multifocal opacities concerning for evolving pneumonia in febrile inpatient."),
            R("Multifocal airspace disease with elevated pneumonia probability in post-operative context.",
                "High suspicion infectious pulmonary process until cultures return.",
                "Infection stewardship consult; oxygen monitoring; repeat imaging per ICU pathway.",
                0.77),
            false));

        list.Add(Mk1(rng, "Sofia", "Andersson", 36, "Female", 7, "Minneapolis, MN",
            "Sharp pleuritic chest pain after long flight; PERC negative; low clinical suspicion PE.",
            "NKDA", "Ibuprofen PRN", "Never smoker", "mild",
            H(2, "UC visit", "Chest pain pleuritic", "Recent travel",
                "HR 88; Wells score low.", "Musculoskeletal vs benign pleurisy",
                "Symptomatic care; return if syncope."),
            S(2, "XRay", "chest_xray_7.png", "/demo-imaging/chest_xray_7.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.09,
                    ["Consolidation"] = 0.07,
                    ["Pleural Effusion"] = 0.11,
                    ["Pneumothorax"] = 0.06,
                    ["Mass/Nodule"] = 0.08,
                    ["Cardiomegaly"] = 0.14
                }),
                SerializeBio(("pleuritic", "SYMPTOM", 0.69)),
                null,
                "CheXNet (demo): essentially clear lungs — PE unlikely based on imaging surrogate alone."),
            R("No convincing acute infiltrate or effusion on simulated chest radiograph.",
                "Low-risk appearance for acute thoracic pathology on this modality.",
                "Clinical PE pathway if pretest probability changes; routine conservative care.",
                0.72),
            false));

        // CT case 1 — multi-slice simulated lung screening
        list.Add(Mk1(rng, "Marcus", "Webb", 63, "Male", 8, "Denver, CO",
            "Annual lung cancer screening CT; former heavy smoker; moderate risk cohort.",
            "NKDA", "Aspirin 81mg", "Former smoker 48 py", "moderate",
            H(30, "Screening clinic", "LDCT scheduled", "Shared decision documented",
                "Reviewed radiation risk.", "Lung cancer screening",
                "Radiology AI adjunct flag for emphysema pattern."),
            S(28, "CTScan", "ct_scan_1.dcm", "/demo-imaging/series/DEMO-LUNG-284736/ct_scan_1.dcm", "application/dicom",
                "LungAI,CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mass/Nodule"] = 0.58,
                    ["Emphysema"] = 0.47,
                    ["Pleural Effusion"] = 0.09,
                    ["Pneumonia"] = 0.11,
                    ["Cardiomegaly"] = 0.16,
                    ["Atelectasis"] = 0.21
                }),
                SerializeBio(("emphysema", "FINDING", 0.68), ("screening", "PROCEDURE", 0.57)),
                J(new LungAICtResponse
                {
                    ScanType = "CT (multi-slice simulated · 287 axial slices)",
                    PredictedClass = "Adenocarcinoma suspicion (low)",
                    ClassNames = ["Normal", "Adenocarcinoma suspicion (low)", "Squamous", "Nodule", "Benign scar"],
                    Probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Normal"] = 0.31,
                        ["Adenocarcinoma suspicion (low)"] = 0.41,
                        ["Squamous"] = 0.12,
                        ["Nodule"] = 0.36,
                        ["Benign scar"] = 0.22
                    },
                    Error = null
                }),
                "LungAI aggregation over 287 axial slices (simulated): mild subsolid nodule RUL 6mm; voting threshold 0.5."),
            R("LDCT demonstrates centrilobular emphysema and a small subsolid right upper lobe nodule (~6 mm) on simulated series.",
                "AI adjunct favors low-grade malignant probability below intervention threshold but above surveillance baseline.",
                "Continue guideline-concordant LDCT intervals; discuss thoracic surgery referral if growth >2mm.",
                0.76),
            true));

        // CT case 2
        list.Add(Mk1(rng, "Diane", "Foster", 55, "Female", 9, "Boston, MA",
            "Incidental pulmonary nodule on abdomen-pelvis CT; dedicated chest CT ordered.",
            "Seasonal rhinitis", "Fluticasone nasal", "Never smoker", "moderate",
            H(18, "Oncology intake", "Incidental nodule", "Prior breast cancer remission",
                "Patient anxious.", "Pulmonary nodule characterization",
                "Dedicated chest CT completed."),
            S(16, "CTScan", "ct_scan_2.dcm", "/demo-imaging/series/DEMO-LUNG-991204/ct_scan_2.dcm", "application/dicom",
                "LungAI,CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Mass/Nodule"] = 0.71,
                    ["Consolidation"] = 0.14,
                    ["Pleural Effusion"] = 0.12,
                    ["Pneumonia"] = 0.13,
                    ["Atelectasis"] = 0.26,
                    ["Cardiomegaly"] = 0.18
                }),
                SerializeBio(("nodule", "FINDING", 0.91), ("spiculated", "FINDING", 0.42)),
                J(new LungAICtResponse
                {
                    ScanType = "CT (multi-slice simulated · 412 axial slices)",
                    PredictedClass = "Nodule — intermediate risk",
                    ClassNames = ["Benign granuloma", "Nodule — intermediate risk", "Adenocarcinoma", "Inflammatory"],
                    Probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Benign granuloma"] = 0.28,
                        ["Nodule — intermediate risk"] = 0.52,
                        ["Adenocarcinoma"] = 0.31,
                        ["Inflammatory"] = 0.24
                    },
                    Error = null
                }),
                "412-slice simulated axial stack: intermediate-risk nodule with mild spiculation — PET discussion if growth."),
            R("Dedicated chest CT (simulated) shows spiculated nodule measuring ~11 mm in left upper lobe.",
                "Intermediate oncologic suspicion; multidisciplinary tumor board consideration.",
                "Short-interval CT or PET-CT per pathway; document smoking history even if never smoker.",
                0.79),
            false));

        list.Add(Mk1(rng, "Yuki", "Tanaka", 29, "Female", 10, "Portland, OR",
            "Young adult with viral URI symptoms; mild cough; low imaging yield expected.",
            "NKDA", "Supportive care only", "Never smoker", "mild",
            H(6, "Walk-in", "Sore throat cough", "Low-grade fever x2d",
                "Afebrile in clinic.", "Viral upper respiratory infection",
                "Supportive care."),
            S(6, "XRay", "chest_xray_8.png", "/demo-imaging/chest_xray_8.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.07,
                    ["Consolidation"] = 0.06,
                    ["Pleural Effusion"] = 0.05,
                    ["Mass/Nodule"] = 0.06,
                    ["Atelectasis"] = 0.09,
                    ["Cardiomegaly"] = 0.08
                }),
                SerializeBio(("URI", "DX", 0.63)),
                null,
                "Near-normal thoracic appearance on simulated CXR."),
            R("No acute cardiopulmonary abnormality detected.",
                "Clinical picture consistent with benign viral illness.",
                "Symptomatic management; precautions for worsening respiratory status.",
                0.81),
            false));

        list.Add(Mk1(rng, "Victor", "Ramos", 76, "Male", 11, "Miami, FL",
            "Severe diffuse infiltrates in setting of nursing-home acquired pneumonia concern.",
            "NKDA", "Metformin; Insulin glargine", "Former smoker", "severe",
            H(1, "Transfer", "Hypoxia", "Bilateral crackles",
                "Requires 4L NC.", "Severe pneumonia",
                "Broad spectrum antibiotics; ICU monitoring."),
            S(1, "XRay", "chest_xray_9.png", "/demo-imaging/chest_xray_9.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pneumonia"] = 0.91,
                    ["Consolidation"] = 0.88,
                    ["Edema"] = 0.55,
                    ["Pleural Effusion"] = 0.47,
                    ["Atelectasis"] = 0.61,
                    ["Mass/Nodule"] = 0.12
                }),
                SerializeBio(("hypoxia", "SYMPTOM", 0.86), ("pneumonia", "DX", 0.9)),
                null,
                "Widespread bilateral airspace opacities — high acuity correlation recommended."),
            R("Diffuse bilateral airspace consolidation with high modeled pneumonia and edema probabilities.",
                "Findings compatible with severe pulmonary infection and possible fluid overload overlap.",
                "ICU-level management; repeat imaging after diuresis/antibiotics as clinically indicated.",
                0.84),
            false));

        list.Add(Mk1(rng, "Hannah", "Müller", 44, "Female", 12, "Detroit, MI",
            "Moderate asthma exacerbation after viral illness; peak flow reduced.",
            "NKDA", "Budesonide-formoterol", "Never smoker", "moderate",
            H(9, "Pulmonary clinic", "Wheeze", "Night symptoms",
                "PF 68% predicted.", "Asthma exacerbation",
                "Burst steroids 7d."),
            S(9, "XRay", "chest_xray_10.png", "/demo-imaging/chest_xray_10.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Atelectasis"] = 0.34,
                    ["Consolidation"] = 0.21,
                    ["Pneumonia"] = 0.24,
                    ["Pleural Effusion"] = 0.08,
                    ["Mass/Nodule"] = 0.11,
                    ["Cardiomegaly"] = 0.13
                }),
                SerializeBio(("asthma", "DX", 0.89), ("exacerbation", "DX", 0.71)),
                null,
                "Peribronchial cuffing subtle — correlate with spirometry."),
            R("Peribronchial thickening without lobar consolidation.",
                "Supports asthma exacerbation over bacterial pneumonia on imaging grounds alone.",
                "Steroid burst per plan; reassess peak flow in 1 week.",
                0.66),
            false));

        list.Add(Mk1(rng, "Omar", "Siddiqui", 52, "Male", 13, "Dallas, TX",
            "Long-term smoker with chronic bronchitic phenotype; mild imaging changes.",
            "NKDA", "Tiotropium", "Current smoker 28 py", "mild",
            H(60, "Primary care", "Chronic cough", "Clear phlegm",
                "Spirometry obstruction.", "Chronic bronchitis",
                "Smoking cessation pharmacotherapy."),
            S(60, "XRay", "chest_xray_11.png", "/demo-imaging/chest_xray_11.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Emphysema"] = 0.36,
                    ["Atelectasis"] = 0.22,
                    ["Mass/Nodule"] = 0.19,
                    ["Pneumonia"] = 0.13,
                    ["Consolidation"] = 0.11,
                    ["Cardiomegaly"] = 0.17
                }),
                SerializeBio(("smoking", "SOCIAL", 0.93), ("bronchitis", "DX", 0.68)),
                null,
                "Hyperinflation pattern subtle on simulated film."),
            R("Findings compatible with chronic obstructive changes without focal pneumonia.",
                "Emphasize cessation and vaccination rather than acute interventions.",
                "Refer to pulmonary rehab; repeat imaging if hemoptysis develops.",
                0.63),
            false));

        list.Add(Mk1(rng, "Emily", "Brooks", 68, "Female", 14, "Philadelphia, PA",
            "Overlap CHF versus pneumonia in elderly with orthopnea and leukocytosis.",
            "Sulfa drugs — rash", "Furosemide, carvedilol", "Never smoker", "severe",
            H(4, "Hospital medicine", "Orthopnea worsening", "Cough pink frothy sputum reported",
                "Bilateral crackles.", "Acute decompensated HF vs pneumonia",
                "IV diuresis; CXR; BNP ordered."),
            S(4, "XRay", "chest_xray_12.png", "/demo-imaging/chest_xray_12.png", "image/png", "CheXNet,BioBERT",
                ChexNetFrom(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Edema"] = 0.82,
                    ["Cardiomegaly"] = 0.74,
                    ["Pleural Effusion"] = 0.51,
                    ["Pneumonia"] = 0.46,
                    ["Consolidation"] = 0.39,
                    ["Mass/Nodule"] = 0.11
                }),
                SerializeBio(("orthopnea", "SYMPTOM", 0.81), ("edema", "FINDING", 0.76)),
                null,
                "Prominent vascular redistribution and effusions — cardiac etiology high probability."),
            R("Cardiomegaly with interstitial edema and bilateral effusions; pneumonia probability intermediate.",
                "Dual pathology possible — prioritize hemodynamic assessment and diuresis while covering infection if indicated.",
                "Serial imaging after therapy; cardiology co-management.",
                0.73),
            false));

        return list;
    }
}
