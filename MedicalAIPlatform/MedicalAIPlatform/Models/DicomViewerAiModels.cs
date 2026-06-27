namespace MedicalAIPlatform.Models;

/// <summary>AI models available in the DICOM Viewer analysis panel (DICOM / volumetric workflows only).</summary>
public static class DicomViewerAiModels
{
    public const string CheXNet = "CheXNet";
    public const string LungAI = "LungAI";

    public static readonly IReadOnlyList<DicomViewerAiModelOption> Options =
    [
        new(CheXNet, ChestXRayModels.CheXNetDisplay, "14-label production chest pathology model for chest DICOM and X-ray."),
        new(LungAI, "LungAI (CT Scan)", "Chest CT classification for CT volumes and compatible DICOM."),
    ];

    public static bool IsSupported(string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && Options.Any(o => string.Equals(o.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string modelId)
    {
        var match = Options.FirstOrDefault(o =>
            string.Equals(o.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? throw new ArgumentException($"Unsupported AI model: {modelId}");
    }
}

public sealed record DicomViewerAiModelOption(string Id, string Label, string Description);
