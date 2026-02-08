using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MedicalAIPlatform.Models
{
    public class PatientViewModel
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string? PatientId { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ProfileImageDataUrl { get; set; }
    }

    public class PatientFormViewModel
    {
        [Required(ErrorMessage = "First name is required")]
        [StringLength(100)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required")]
        [StringLength(100)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Date of birth is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Date of Birth")]
        public DateTime DateOfBirth { get; set; } = DateTime.Now.AddYears(-30);

        [Required(ErrorMessage = "Gender is required")]
        [StringLength(10)]
        [Display(Name = "Gender")]
        public string Gender { get; set; } = "Male";

        [StringLength(20)]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [StringLength(200)]
        [EmailAddress]
        [Display(Name = "Email")]
        public string? Email { get; set; }

        [StringLength(500)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [StringLength(50)]
        [Display(Name = "Patient ID (Medical Record Number)")]
        public string? PatientId { get; set; }

        [StringLength(500)]
        [Display(Name = "Medical History Summary")]
        public string? MedicalHistorySummary { get; set; }

        [StringLength(500)]
        [Display(Name = "Allergies")]
        public string? Allergies { get; set; }

        [StringLength(500)]
        [Display(Name = "Current Medications")]
        public string? CurrentMedications { get; set; }

        [Display(Name = "Profile Image")]
        public IFormFile? ProfileImage { get; set; }

        // History entry fields
        [DataType(DataType.DateTime)]
        [Display(Name = "Visit Date")]
        public DateTime? VisitDate { get; set; }

        [StringLength(200)]
        [Display(Name = "Visit Type")]
        public string? VisitType { get; set; }

        [StringLength(1000)]
        [Display(Name = "Chief Complaint")]
        public string? ChiefComplaint { get; set; }

        [StringLength(5000)]
        [Display(Name = "Clinical Notes")]
        public string? ClinicalNotes { get; set; }

        [StringLength(5000)]
        [Display(Name = "Condition Description")]
        public string? ConditionDescription { get; set; }

        [StringLength(5000)]
        [Display(Name = "Diagnosis")]
        public string? Diagnosis { get; set; }

        [StringLength(5000)]
        [Display(Name = "Treatment Plan")]
        public string? TreatmentPlan { get; set; }

        // Scan uploads
        [Display(Name = "X-Ray Image")]
        public IFormFile? XRayImage { get; set; }

        [Display(Name = "CT Scan Image")]
        public IFormFile? CTScanImage { get; set; }
    }

    public class PatientDetailsViewModel
    {
        public Patient Patient { get; set; } = null!;
        public string? ProfileImageDataUrl { get; set; }
        public List<PatientHistory> HistoryEntries { get; set; } = new List<PatientHistory>();
        public List<PatientScanDetailViewModel> Scans { get; set; } = new List<PatientScanDetailViewModel>();
    }

    public class PatientScanDetailViewModel
    {
        public int Id { get; set; }
        public string ScanType { get; set; } = string.Empty;
        public DateTime ScanDate { get; set; }
        public string? FileName { get; set; }
        public string? ImageDataUrl { get; set; }
        public string? LinkedModels { get; set; }
        public string? GeneratedResult { get; set; }
        public DateTime? ResultGeneratedAt { get; set; }
    }
}
