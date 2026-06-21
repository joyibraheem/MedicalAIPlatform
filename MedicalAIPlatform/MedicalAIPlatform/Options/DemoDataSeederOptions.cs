namespace MedicalAIPlatform.Options;

/// <summary>Toggle synthetic demo patients/scans/reports for QA (prefix <c>DEMO-</c> on MRN).</summary>
public sealed class DemoDataSeederOptions
{
    public const string SectionName = "DemoData";

    /// <summary>When true, seeds demo data once after migrations if no DEMO- patients exist.</summary>
    public bool RunOnStartup { get; set; }

    /// <summary>Optional shared secret for POST /api/admin/demo-data/seed when non-empty.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>RNG seed for reproducible visit dates / tie-breaks.</summary>
    public int RandomSeed { get; set; } = 20260201;

    /// <summary>Medical report modelVersion string stored on generated reports.</summary>
    public string ReportModelVersion { get; set; } = "demo-seed-v1";
}
