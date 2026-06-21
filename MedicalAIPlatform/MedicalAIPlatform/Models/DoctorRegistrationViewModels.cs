using System.ComponentModel.DataAnnotations;

namespace MedicalAIPlatform.Models;

public sealed class CompleteDoctorProfileViewModel
{
    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(120)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Medical specialty is required.")]
    [StringLength(120)]
    [Display(Name = "Medical specialty")]
    public string Specialization { get; set; } = "";

    [Required(ErrorMessage = "Hospital or organization is required.")]
    [StringLength(256)]
    [Display(Name = "Hospital / organization")]
    public string HospitalOrganization { get; set; } = "";

    [Required(ErrorMessage = "Medical license number is required.")]
    [StringLength(64)]
    [Display(Name = "Medical license number")]
    public string MedicalLicenseNumber { get; set; } = "";

    [Required(ErrorMessage = "Phone number is required.")]
    [Phone]
    [StringLength(32)]
    [Display(Name = "Phone number")]
    public string PhoneNumber { get; set; } = "";

    [StringLength(2000)]
    [Display(Name = "Additional notes (optional)")]
    public string? RegistrationNotes { get; set; }
}

public sealed class ApprovalPendingViewModel
{
    public string Status { get; set; } = DoctorRegistrationStatuses.Pending;
    public string? RejectionReason { get; set; }
    public bool ProfileSubmitted { get; set; }
}

public sealed class RejectDoctorViewModel
{
    [Required]
    public string UserId { get; set; } = "";

    [Required(ErrorMessage = "Please provide a rejection reason.")]
    [StringLength(2000)]
    [Display(Name = "Rejection reason")]
    public string RejectionReason { get; set; } = "";
}

public sealed class DoctorRegistrationDetailsViewModel
{
    public string UserId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? FullName { get; set; }
    public string? Specialization { get; set; }
    public string? HospitalOrganization { get; set; }
    public string? MedicalLicenseNumber { get; set; }
    public string? PhoneNumber { get; set; }
    public string? RegistrationNotes { get; set; }
    public string DoctorStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTimeOffset? ProfileSubmittedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? ApprovedByAdminName { get; set; }
    public string? RejectedByAdminName { get; set; }
}
