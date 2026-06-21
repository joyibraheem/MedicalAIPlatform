namespace MedicalAIPlatform.Options;

public sealed class DoctorRegistrationOptions
{
    public const string SectionName = "DoctorRegistration";

    public string PlatformDisplayName { get; set; } = "Medical AI Platform";

    /// <summary>Comma-separated fallback admin emails when no Admin-role users exist.</summary>
    public string? FallbackAdminNotificationEmails { get; set; }
}
