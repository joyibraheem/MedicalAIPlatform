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

        // AI Model Results Storage (JSON strings)
        [StringLength(10000)]
        public string? CheXNetResults { get; set; } // JSON serialized CheXNetPredictionResponse

        [StringLength(10000)]
        public string? BioBertResults { get; set; } // JSON serialized BioBertResponse

        [StringLength(10000)]
        public string? LungAIResults { get; set; } // JSON serialized LungAICtResponse

        // Linked AI Models (comma-separated list of model names)
        [StringLength(200)]
        public string? LinkedModels { get; set; } // e.g., "CheXNet,BioBert,LungAI"

        // Generated result summary
        [StringLength(5000)]
        public string? GeneratedResult { get; set; }

        [DataType(DataType.DateTime)]
        public DateTime? ResultGeneratedAt { get; set; }

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
    }
}
