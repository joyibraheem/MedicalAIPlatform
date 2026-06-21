namespace MedicalAIPlatform.Models;

/// <summary>Lifecycle states for the scan AI analysis pipeline.</summary>
public static class ScanAiAnalysisStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
