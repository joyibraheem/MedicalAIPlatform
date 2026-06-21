using System.Text.RegularExpressions;

namespace MedicalAIPlatform.Services;

/// <summary>Detects structured-report chat intents and resolves patient context.</summary>
public static class MedicalReportIntentParser
{
    private static readonly Regex GenerateReportRx = new(
        @"\b(generate|create|build|draft)\s+(a\s+)?(structured\s+)?(medical\s+)?report\b|\b(medical\s+)?report\b.*\b(generate|create|build|draft)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExplainReportRx = new(
        @"\b(explain|summarize|summary|interpret|clarify|break\s+down)\b.*\b(report|findings|impression)\b|\b(report|findings)\b.*\b(explain|summarize|summary|interpret)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PatientIdRx = new(
        @"\b(?:patient|chart|mrn|record)\s*(?:id|#|:)?\s*(\d{1,8})\b|\bid\s*[#:]?\s*(\d{1,8})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsGenerateMedicalReportIntent(string? message) =>
        !string.IsNullOrWhiteSpace(message) && GenerateReportRx.IsMatch(message.Trim());

    public static bool IsExplainMedicalReportIntent(string? message) =>
        !string.IsNullOrWhiteSpace(message) && ExplainReportRx.IsMatch(message.Trim());

    public static bool TryResolvePatientId(string? message, int? patientContextId, out int patientId)
    {
        patientId = 0;
        if (patientContextId is > 0)
        {
            patientId = patientContextId.Value;
            return true;
        }

        if (string.IsNullOrWhiteSpace(message))
            return false;

        var m = PatientIdRx.Match(message);
        if (!m.Success)
            return false;

        var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return int.TryParse(g, out patientId) && patientId > 0;
    }
}
