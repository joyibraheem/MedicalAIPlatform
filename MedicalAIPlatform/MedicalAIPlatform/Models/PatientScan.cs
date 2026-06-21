using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models
{
    public class PatientScan
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PatientId { get; set; }

        [Required]
        [StringLength(50)]
        public string ScanType { get; set; } = string.Empty; // XRay, CTScan, MRI, etc.

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime ScanDate { get; set; } = DateTime.UtcNow;

        [StringLength(200)]
        public string? FileName { get; set; }

        [StringLength(100)]
        public string? ContentType { get; set; }

        // Store image as byte array or file path
        public byte[]? ImageData { get; set; }

        [StringLength(1000)]
        public string? ImagePath { get; set; }

        // Store base64 data URL for quick display
        [StringLength(50000)]
        public string? ImageDataUrl { get; set; }

        // Link to patient history entry if this scan is associated with a visit
        public int? PatientHistoryId { get; set; }

        // Foreign key to doctor/user who uploaded this scan
        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("PatientId")]
        public virtual Patient Patient { get; set; } = null!;

        [ForeignKey("PatientHistoryId")]
        public virtual PatientHistory? PatientHistory { get; set; }

        [ForeignKey("CreatedByUserId")]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        /// <summary>Exactly one AI analysis row per scan (created when the scan is registered).</summary>
        public virtual ScanAiAnalysis? AiAnalysis { get; set; }
    }
}
