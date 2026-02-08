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
    public string DoctorStatus { get; set; } = "None";
}


