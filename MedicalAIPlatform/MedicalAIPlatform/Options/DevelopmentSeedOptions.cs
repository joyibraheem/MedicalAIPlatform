namespace MedicalAIPlatform.Options;

/// <summary>Development-only bootstrap accounts. Passwords must come from env vars or user secrets — never committed.</summary>
public sealed class DevelopmentSeedOptions
{
    public const string SectionName = "DevelopmentSeed";

    /// <summary>When false, no default admin/doctor accounts are created even in Development.</summary>
    public bool Enabled { get; set; } = true;

    public string AdminEmail { get; set; } = "admin@medicalai.com";

    public string DoctorEmail { get; set; } = "doctor@medicalai.com";

    /// <summary>Optional fallback when env vars are unset (use dotnet user-secrets locally).</summary>
    public string? AdminPassword { get; set; }

    public string? DoctorPassword { get; set; }
}
