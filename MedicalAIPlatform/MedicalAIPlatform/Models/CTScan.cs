using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models;

public class CTScan
{
    [Key]
    public int Id { get; set; }

    public int? PatientId { get; set; }

    [StringLength(200)]
    public string? PatientName { get; set; }

    [Required]
    [StringLength(200)]
    public string ScanTitle { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ScanType { get; set; } = "CT";

    [StringLength(1000)]
    public string? ImagePath { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    [StringLength(200)]
    public string? FileName { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? PredictionResult { get; set; }

    public double? Confidence { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    [StringLength(50)]
    public string Status { get; set; } = "Uploaded";

    public int SeriesIndex { get; set; } = 1;

    public int SeriesTotal { get; set; } = 1;

    [StringLength(100)]
    public string BodyPart { get; set; } = "CHEST";

    [StringLength(450)]
    public string? CreatedByUserId { get; set; }

    [ForeignKey(nameof(PatientId))]
    public virtual Patient? Patient { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual ApplicationUser? CreatedByUser { get; set; }
}
