namespace MedicalAIPlatform.Models;

/// <summary>Outcome of <see cref="Services.MedicalPlatformDemoDataSeeder"/>.</summary>
public sealed class DemoSeedResultDto
{
    public bool Skipped { get; set; }

    public string Message { get; set; } = "";

    public int PatientsCreated { get; set; }

    public int HistoryEntriesCreated { get; set; }

    public int ScansCreated { get; set; }

    public int ReportsCreated { get; set; }
}
