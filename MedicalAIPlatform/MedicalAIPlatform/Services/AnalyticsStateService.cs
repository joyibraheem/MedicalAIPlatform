using System.Security.Claims;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

/// <summary>
/// Request-scoped facade over per-user analytics session storage.
/// Views inject this service; background workers use <see cref="IUserAnalyticsSessionStore"/> directly.
/// </summary>
public sealed class AnalyticsStateService
{
    private readonly IUserAnalyticsSessionStore _store;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private AnalyticsSessionSnapshot? _requestOverride;

    public AnalyticsStateService(IUserAnalyticsSessionStore store, IHttpContextAccessor httpContextAccessor)
    {
        _store = store;
        _httpContextAccessor = httpContextAccessor;
    }

    public Dictionary<string, CheXNetPredictionResponse>? CurrentResults => GetEffectiveSnapshot().CurrentResults;
    public BioBertResponse? CurrentBioBertResults => GetEffectiveSnapshot().CurrentBioBertResults;
    public LungAICtResponse? CurrentLungAIResults => GetEffectiveSnapshot().CurrentLungAIResults;
    public IReadOnlyList<string> PipelineNotes => GetEffectiveSnapshot().PipelineNotes;
    public string? XRayImageDataUrl => GetEffectiveSnapshot().XRayImageDataUrl;
    public string? CTImageDataUrl => GetEffectiveSnapshot().CTImageDataUrl;
    public string? ClinicalText => GetEffectiveSnapshot().ClinicalText;
    public string? CheXNetSourceJson => GetEffectiveSnapshot().CheXNetSourceJson;
    public string? LungCancerSourceJson => GetEffectiveSnapshot().LungCancerSourceJson;
    public string? BioBertSourceJson => GetEffectiveSnapshot().BioBertSourceJson;
    public Guid? RelatedJobId => GetEffectiveSnapshot().RelatedJobId;

    /// <summary>Hydrates this request from a loaded snapshot (e.g. job-specific result page).</summary>
    public void HydrateForRequest(AnalyticsSessionSnapshot snapshot) => _requestOverride = snapshot;

    public void SetResults(
        Dictionary<string, CheXNetPredictionResponse>? chexnetResults,
        BioBertResponse? bioBertResults,
        LungAICtResponse? lungAIResults = null,
        string? xrayImageDataUrl = null,
        string? ctImageDataUrl = null,
        string? clinicalText = null,
        IReadOnlyList<string>? pipelineNotes = null,
        string? cheXNetSourceJson = null,
        string? lungCancerSourceJson = null,
        string? bioBertSourceJson = null,
        Guid? relatedJobId = null)
    {
        var userId = RequireUserId();
        var patch = AnalyticsSessionSnapshot.FromSetResults(
            chexnetResults,
            bioBertResults,
            lungAIResults,
            xrayImageDataUrl,
            ctImageDataUrl,
            clinicalText,
            pipelineNotes,
            pipelineNotesProvided: pipelineNotes is not null,
            cheXNetSourceJson,
            lungCancerSourceJson,
            bioBertSourceJson,
            relatedJobId);

        _store.MergeLatest(userId, patch);
        _requestOverride = _store.GetLatest(userId) ?? patch;
    }

    public void ClearResults()
    {
        var userId = TryGetUserId();
        if (userId is null)
            return;

        _store.Clear(userId);
        _requestOverride = AnalyticsSessionSnapshot.Empty;
    }

    private AnalyticsSessionSnapshot GetEffectiveSnapshot()
    {
        if (_requestOverride is not null)
            return _requestOverride;

        var userId = TryGetUserId();
        if (userId is null)
            return AnalyticsSessionSnapshot.Empty;

        return _store.GetLatest(userId) ?? AnalyticsSessionSnapshot.Empty;
    }

    private string? TryGetUserId() =>
        _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    private string RequireUserId() =>
        TryGetUserId()
        ?? throw new InvalidOperationException("Analytics state requires an authenticated user.");
}
