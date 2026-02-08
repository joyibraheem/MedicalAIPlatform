using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public sealed class AnalyticsStateService
{
    public Dictionary<string, CheXNetPredictionResponse>? CurrentResults { get; private set; }
    public BioBertResponse? CurrentBioBertResults { get; private set; }
    public LungAICtResponse? CurrentLungAIResults { get; private set; }
    
    public string? XRayImageDataUrl { get; private set; }
    public string? CTImageDataUrl { get; private set; }
    public string? ClinicalText { get; private set; }

    public void SetResults(
        Dictionary<string, CheXNetPredictionResponse>? chexnetResults,
        BioBertResponse? bioBertResults,
        LungAICtResponse? lungAIResults = null,
        string? xrayImageDataUrl = null,
        string? ctImageDataUrl = null,
        string? clinicalText = null)
    {
        CurrentResults = chexnetResults;
        CurrentBioBertResults = bioBertResults;
        CurrentLungAIResults = lungAIResults;
        XRayImageDataUrl = xrayImageDataUrl;
        CTImageDataUrl = ctImageDataUrl;
        ClinicalText = clinicalText;
    }

    public void ClearResults()
    {
        CurrentResults = null;
        CurrentBioBertResults = null;
        CurrentLungAIResults = null;
        XRayImageDataUrl = null;
        CTImageDataUrl = null;
        ClinicalText = null;
    }
}
