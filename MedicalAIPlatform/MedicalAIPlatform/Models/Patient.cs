using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models
{
    public class Patient
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";

        [Required]
        [DataType(DataType.Date)]
        public DateTime DateOfBirth { get; set; }

        [Required]
        [StringLength(10)]
        public string Gender { get; set; } = string.Empty; // Male, Female, Other

        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        [StringLength(200)]
        [EmailAddress]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(50)]
        public string? PatientId { get; set; } // Medical record number

        [StringLength(500)]
        public string? MedicalHistorySummary { get; set; }

        [StringLength(500)]
        public string? Allergies { get; set; }

        [StringLength(500)]
        public string? CurrentMedications { get; set; }

        // Profile image stored as base64 or file path
        [StringLength(1000)]
        public string? ProfileImagePath { get; set; }

        public byte[]? ProfileImageData { get; set; }

        [StringLength(50)]
        public string? ProfileImageContentType { get; set; }

        // Foreign key to doctor/user who created this patient record
        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("CreatedByUserId")]
        public virtual ApplicationUser? CreatedByUser { get; set; }

        public virtual ICollection<PatientHistory> HistoryEntries { get; set; } = new List<PatientHistory>();
        public virtual ICollection<PatientScan> Scans { get; set; } = new List<PatientScan>();
        public virtual ICollection<ClinicalMedicalReport> MedicalReports { get; set; } = new List<ClinicalMedicalReport>();
    }
}
