namespace MedicalAIPlatform.Models;

/// <summary>Registry for modular chest X-ray inference backends under CheXNet-master/models/.</summary>
public static class ChestXRayModels
{
    public const string CheXNet = "CheXNet";
    public const string BraxRaddino = "BRAX_RADDINO";

    public const string CheXNetDisplay = "CheXNet (Production)";
    public const string BraxRaddinoDisplay = "BRAX Fine-Tuned (RAD-DINO)";

    public const string CheXNetStatus = "Production";
    public const string BraxRaddinoStatus = "Research / Fine-Tuned";

    public static readonly IReadOnlyList<ChestXRayModelOption> Options =
    [
        new(CheXNet, CheXNetDisplay, CheXNetStatus, "Default production DenseNet121 CheXNet model."),
        new(BraxRaddino, BraxRaddinoDisplay, BraxRaddinoStatus, "BRAX fine-tuned RAD-DINO research model."),
    ];

    public static bool IsSupported(string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && Options.Any(o => string.Equals(o.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return CheXNet;

        var match = Options.FirstOrDefault(o =>
            string.Equals(o.Id, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? CheXNet;
    }

    public static string GetResultKey(string modelId) => Normalize(modelId);

    public static string GetDisplayName(string modelId) =>
        Options.FirstOrDefault(o => string.Equals(o.Id, Normalize(modelId), StringComparison.Ordinal))
            ?.Label
        ?? Normalize(modelId);

    public static string GetStatus(string modelId) =>
        Options.FirstOrDefault(o => string.Equals(o.Id, Normalize(modelId), StringComparison.Ordinal))
            ?.Status
        ?? CheXNetStatus;

    public static ChestXRayModelOption? Find(string? modelId)
    {
        var id = Normalize(modelId);
        return Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));
    }
}

public sealed record ChestXRayModelOption(string Id, string Label, string Status, string Description);
