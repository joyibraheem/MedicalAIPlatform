using System.ComponentModel.DataAnnotations;

namespace MedicalAIPlatform.Models;

public class SettingsViewModel
{
    [Display(Name = "Enable dark mode")]
    public bool IsDarkMode { get; set; }

    [Required]
    [Display(Name = "Language")]
    public string Language { get; set; } = "en";

    public string FullName { get; set; } = "Dr. Guest";
    public string Email { get; set; } = "";
    public string Specialization { get; set; } = "General Practitioner";
    public string UserId { get; set; } = "";

    /// <summary>Display label for clinical role (Administrator, Doctor, Guest).</summary>
    public string RoleDisplay { get; set; } = "Guest";

    public string AccountStatusDisplay { get; set; } = "Active";

    /// <summary>Shown when last sign-in is not tracked server-side (placeholder for UX).</summary>
    public string LastLoginDisplay { get; set; } = "—";

    public string AppVersion { get; set; } = "1.0";
}


