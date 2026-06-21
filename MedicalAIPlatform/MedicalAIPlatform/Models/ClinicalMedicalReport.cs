using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models;

/// <summary>Persisted structured thoracic medical report (AI-generated baseline + optional doctor edits).</summary>
public sealed class ClinicalMedicalReport
{
    public Guid Id { get; set; }

    [Required]
    public int PatientId { get; set; }

    [ForeignKey(nameof(PatientId))]
    public Patient Patient { get; set; } = null!;

    /// <summary>Source AI analysis (1:1). Null for legacy patient-level reports migrated before this link existed.</summary>
    public int? ScanAiAnalysisId { get; set; }

    [ForeignKey(nameof(ScanAiAnalysisId))]
    public ScanAiAnalysis? ScanAiAnalysis { get; set; }

    [Required]
    [StringLength(450)]
    public string GeneratedByUserId { get; set; } = "";

    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Immutable JSON snapshot produced by the report generator at creation time.</summary>
    public string AiSnapshotJson { get; set; } = "{}";

    [Required]
    public string FindingsDisplay { get; set; } = "";

    [Required]
    public string ImpressionDisplay { get; set; } = "";

    [Required]
    public string RecommendationsDisplay { get; set; } = "";

    public double ConfidenceSnapshot { get; set; }

    [StringLength(128)]
    public string ModelVersion { get; set; } = "";

    public bool IsDoctorModified { get; set; }

    public DateTimeOffset? DoctorModifiedAt { get; set; }

    [StringLength(450)]
    public string? DoctorModifiedByUserId { get; set; }

    public ICollection<MedicalReportRevision> Revisions { get; set; } = new List<MedicalReportRevision>();
}

/// <summary>Version history row for clinical sections.</summary>
public sealed class MedicalReportRevision
{
    public long Id { get; set; }

    public Guid ReportId { get; set; }

    [ForeignKey(nameof(ReportId))]
    public ClinicalMedicalReport Report { get; set; } = null!;

    /// <summary>ai_generated | doctor_edit | revert_to_ai</summary>
    [Required]
    [StringLength(32)]
    public string Source { get; set; } = "";

    [Required]
    public string Findings { get; set; } = "";

    [Required]
    public string Impression { get; set; } = "";

    [Required]
    public string Recommendations { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    [StringLength(450)]
    public string? ActorUserId { get; set; }
}
