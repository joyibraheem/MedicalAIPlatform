using System;
using Microsoft.AspNetCore.Identity;

namespace MedicalAIPlatform.Models;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }

    /// <summary>
    /// Medical specialization for doctors (e.g. Cardiology, Neurology).
    /// Can be null for non-doctor roles.
    /// </summary>
    public string? Specialization { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDarkMode { get; set; } = false;

    public string Language { get; set; } = "en";

    /// <summary>
    /// Status of doctor account: "Pending", "Verified", "Rejected", or "None" (for patients).
    /// </summary>
    public string DoctorStatus { get; set; } = DoctorRegistrationStatuses.None;

    /// <summary>When true, user must set a new password before accessing medical features (dev seed accounts).</summary>
    public bool MustChangePasswordOnLogin { get; set; }

    public string? HospitalOrganization { get; set; }

    public string? MedicalLicenseNumber { get; set; }

    /// <summary>Optional notes supplied during registration profile completion.</summary>
    public string? RegistrationNotes { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>Set when the doctor submits the profile completion form for admin review.</summary>
    public DateTimeOffset? ProfileSubmittedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? RejectedAt { get; set; }

    public string? ApprovedByAdminId { get; set; }

    public string? RejectedByAdminId { get; set; }
}


