using CheXNet.BlazorApp.Models;

namespace CheXNet.BlazorApp.Services;

/// <summary>
/// State service to share CheXNet (X-ray), BioBERT (text), and LungAI (CT) results between pages (Analyze -> Chat).
/// </summary>
public sealed class CheXNetStateService
{
    public Dictionary<string, CheXNetPredictionResponse>? CurrentResults { get; private set; }
    public BioBertResponse? CurrentBioBertResults { get; private set; }
    public LungAICtResponse? CurrentLungAIResults { get; private set; }
    
    // Store image data for display on result pages
    public string? XRayImageDataUrl { get; private set; }
    public string? CTImageDataUrl { get; private set; }
    public string? ClinicalText { get; private set; }

    public event Action? ResultsChanged;

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
        ResultsChanged?.Invoke();
    }

    public void ClearResults()
    {
        CurrentResults = null;
        CurrentBioBertResults = null;
        CurrentLungAIResults = null;
        XRayImageDataUrl = null;
        CTImageDataUrl = null;
        ClinicalText = null;
        ResultsChanged?.Invoke();
    }
}
