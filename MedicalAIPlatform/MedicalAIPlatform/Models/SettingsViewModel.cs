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
}


