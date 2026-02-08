using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MedicalAIPlatform.Models
{
    public class PatientHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PatientId { get; set; }

        [Required]
        [DataType(DataType.DateTime)]
        public DateTime VisitDate { get; set; } = DateTime.UtcNow;

        [StringLength(200)]
        public string? VisitType { get; set; } // Checkup, Consultation, Emergency, Follow-up

        [StringLength(1000)]
        public string? ChiefComplaint { get; set; }

        [StringLength(5000)]
        public string? ClinicalNotes { get; set; }

        [StringLength(5000)]
        public string? Diagnosis { get; set; }

        [StringLength(5000)]
        public string? TreatmentPlan { get; set; }

        [StringLength(5000)]
        public string? ConditionDescription { get; set; } // Text describing patient's condition

        // Foreign key to doctor/user who created this entry
        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("PatientId")]
        public virtual Patient Patient { get; set; } = null!;

        [ForeignKey("CreatedByUserId")]
        public virtual ApplicationUser? CreatedByUser { get; set; }
    }
}
