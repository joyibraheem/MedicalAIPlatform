using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

/// <summary>Shared helpers for reading chest X-ray results from analytics session dictionaries.</summary>
public static class ChestXRayResultHelper
{
    public static bool TryGetResult(
        Dictionary<string, CheXNetPredictionResponse>? results,
        string? selectedModelId,
        out CheXNetPredictionResponse? result,
        out string resultKey)
    {
        result = null;
        resultKey = ChestXRayModels.Normalize(selectedModelId);

        if (results is null || results.Count == 0)
            return false;

        if (results.TryGetValue(resultKey, out result))
            return true;

        if (results.TryGetValue(ChestXRayModels.CheXNet, out result))
        {
            resultKey = ChestXRayModels.CheXNet;
            return true;
        }

        if (results.TryGetValue(ChestXRayModels.BraxRaddino, out result))
        {
            resultKey = ChestXRayModels.BraxRaddino;
            return true;
        }

        var first = results.First();
        resultKey = first.Key;
        result = first.Value;
        return true;
    }

    public static string GetTopPrediction(CheXNetPredictionResponse result)
    {
        if (!string.IsNullOrWhiteSpace(result.PredictedClass))
            return result.PredictedClass!;

        if (result.TopK.FirstOrDefault() is { ClassName: { Length: > 0 } cn })
            return cn;

        if (result.Probabilities.Count > 0)
            return result.Probabilities.OrderByDescending(kv => kv.Value).First().Key;

        return "Unknown";
    }

    public static double? GetTopConfidence(CheXNetPredictionResponse result) =>
        result.Confidence
        ?? result.TopK.FirstOrDefault()?.Probability
        ?? (result.Probabilities.Count > 0
            ? result.Probabilities.OrderByDescending(kv => kv.Value).First().Value
            : null);
}
