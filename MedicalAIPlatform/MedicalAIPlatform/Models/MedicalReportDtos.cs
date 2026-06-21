using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

/// <summary>Structured AI snapshot stored in <see cref="ClinicalMedicalReport.AiSnapshotJson"/>.</summary>
public sealed class MedicalReportAiSnapshot
{
    [JsonPropertyName("medical_history")]
    public MedicalReportMedicalHistoryDto MedicalHistory { get; set; } = new();

    /// <summary>Merged model probabilities + provenance for UI/reports (persisted).</summary>
    [JsonPropertyName("ai_analysis")]
    public MedicalReportAiAnalysisSnapshotDto AiAnalysis { get; set; } = new();

    [JsonPropertyName("findings")]
    public string Findings { get; set; } = "";

    [JsonPropertyName("impression")]
    public string Impression { get; set; } = "";

    [JsonPropertyName("recommendations")]
    public string Recommendations { get; set; } = "";

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("confidence_method")]
    public string ConfidenceMethod { get; set; } = "";

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; set; } = "";

    [JsonPropertyName("optional_images")]
    public List<MedicalReportImageRefDto> OptionalImages { get; set; } = [];
}

/// <summary>Persisted AI probability block inside <see cref="MedicalReportAiSnapshot"/>.</summary>
public sealed class MedicalReportAiAnalysisSnapshotDto
{
    [JsonPropertyName("chexnet_probabilities")]
    public List<MedicalReportProbabilitySnapshotDto> ChexnetProbabilities { get; set; } = [];

    [JsonPropertyName("ct_probabilities")]
    public List<MedicalReportProbabilitySnapshotDto> CtProbabilities { get; set; } = [];

    [JsonPropertyName("clinical_entities")]
    public List<MedicalReportEntitySnapshotDto> ClinicalEntities { get; set; } = [];

    [JsonPropertyName("top_condition_label")]
    public string TopConditionLabel { get; set; } = "";

    [JsonPropertyName("top_condition_probability")]
    public double TopConditionProbability { get; set; }

    [JsonPropertyName("risk_tier")]
    public string RiskTier { get; set; } = "";

    [JsonPropertyName("confidence_caption")]
    public string ConfidenceCaption { get; set; } = "";

    [JsonPropertyName("heatmap_note")]
    public string HeatmapNote { get; set; } = "";

    [JsonPropertyName("used_patient_studies")]
    public bool UsedPatientStudies { get; set; }

    [JsonPropertyName("used_live_session")]
    public bool UsedLiveSession { get; set; }
}

public sealed class MedicalReportProbabilitySnapshotDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("probability")]
    public double Probability { get; set; }

    /// <summary>Human-readable provenance e.g. stored CXR vs live CheXNet session.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
}

public sealed class MedicalReportEntitySnapshotDto
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("group")]
    public string Group { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

public sealed class MedicalReportMedicalHistoryDto
{
    [JsonPropertyName("demographics")]
    public Dictionary<string, string> Demographics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("summary_lines")]
    public List<string> SummaryLines { get; set; } = [];

    [JsonPropertyName("visits")]
    public List<MedicalReportVisitDto> Visits { get; set; } = [];
}

public sealed class MedicalReportVisitDto
{
    [JsonPropertyName("visit_date")]
    public string? VisitDate { get; set; }

    [JsonPropertyName("visit_type")]
    public string? VisitType { get; set; }

    [JsonPropertyName("chief_complaint")]
    public string? ChiefComplaint { get; set; }

    [JsonPropertyName("diagnosis")]
    public string? Diagnosis { get; set; }

    [JsonPropertyName("clinical_notes")]
    public string? ClinicalNotes { get; set; }
}

public sealed class MedicalReportImageRefDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("data_url")]
    public string DataUrl { get; set; } = "";
}

/// <summary>API / UI payload for a persisted report.</summary>
public sealed class MedicalReportResponseDto
{
    public Guid Id { get; set; }

    public int PatientId { get; set; }

    public int? PatientScanId { get; set; }

    public int? ScanAiAnalysisId { get; set; }

    public string PatientDisplayName { get; set; } = "";

    /// <summary>ai_generated | doctor_modified</summary>
    public string StatusBadge { get; set; } = "";

    public MedicalReportMedicalHistoryDto MedicalHistory { get; set; } = new();

    public string Findings { get; set; } = "";

    public string Impression { get; set; } = "";

    public string Recommendations { get; set; } = "";

    public double Confidence { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public string ModelVersion { get; set; } = "";

    public DateTimeOffset? DoctorModifiedAt { get; set; }

    public List<MedicalReportImageRefDto> OptionalImages { get; set; } = [];

    /// <summary>Structured merged AI outputs (CheXNet / LungAI / BioBERT).</summary>
    public MedicalReportAiAnalysisDto AiAnalysis { get; set; } = new();

    /// <summary>How <see cref="Confidence"/> was derived.</summary>
    public string ConfidenceMethod { get; set; } = "";

    /// <summary>Original AI sections (from snapshot) for revert UX.</summary>
    public MedicalReportAiSectionsDto AiOriginal { get; set; } = new();
}

public sealed class MedicalReportAiAnalysisDto
{
    public List<MedicalReportProbabilityRowDto> ChexnetProbabilities { get; set; } = [];

    public List<MedicalReportProbabilityRowDto> CtProbabilities { get; set; } = [];

    public List<MedicalReportEntityRowDto> ClinicalEntities { get; set; } = [];

    public string TopConditionLabel { get; set; } = "";

    public double TopConditionProbability { get; set; }

    /// <summary>high | medium | low | minimal</summary>
    public string RiskTier { get; set; } = "";

    public string ConfidenceCaption { get; set; } = "";

    public string HeatmapNote { get; set; } = "";

    public bool UsedPatientStudies { get; set; }

    public bool UsedLiveSession { get; set; }
}

public sealed class MedicalReportProbabilityRowDto
{
    public string Label { get; set; } = "";

    public double Probability { get; set; }

    public string Source { get; set; } = "";
}

public sealed class MedicalReportEntityRowDto
{
    public string Text { get; set; } = "";

    public string Group { get; set; } = "";

    public double Score { get; set; }
}

public sealed class MedicalReportAiSectionsDto
{
    public string Findings { get; set; } = "";

    public string Impression { get; set; } = "";

    public string Recommendations { get; set; } = "";
}

public sealed class MedicalReportClinicalEditDto
{
    public string Findings { get; set; } = "";

    public string Impression { get; set; } = "";

    public string Recommendations { get; set; } = "";
}

public sealed class MedicalReportGenerateRequestDto
{
    public int PatientId { get; set; }

    /// <summary>When set, generates the report for this specific scan's AI analysis.</summary>
    public int? PatientScanId { get; set; }
}
