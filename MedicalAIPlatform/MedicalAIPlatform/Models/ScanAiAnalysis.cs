using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models;

/// <summary>
/// AI inference output for exactly one imaging study (<see cref="PatientScan"/>).
/// One scan → one analysis row; one analysis → at most one <see cref="ClinicalMedicalReport"/>.
/// </summary>
public sealed class ScanAiAnalysis
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int PatientScanId { get; set; }

    [ForeignKey(nameof(PatientScanId))]
    public PatientScan PatientScan { get; set; } = null!;

    /// <see cref="ScanAiAnalysisStatuses"/>
    [Required]
    [StringLength(32)]
    public string Status { get; set; } = ScanAiAnalysisStatuses.Pending;

    [StringLength(10000)]
    public string? CheXNetResults { get; set; }

    [StringLength(10000)]
    public string? BioBertResults { get; set; }

    [StringLength(10000)]
    public string? LungAIResults { get; set; }

    [StringLength(200)]
    public string? LinkedModels { get; set; }

    [StringLength(5000)]
    public string? GeneratedResult { get; set; }

    public DateTime? ResultGeneratedAt { get; set; }

    public Guid? BackgroundJobId { get; set; }

    [ForeignKey(nameof(BackgroundJobId))]
    public ChestAiBackgroundJob? BackgroundJob { get; set; }

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    [StringLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ClinicalMedicalReport? MedicalReport { get; set; }
}
