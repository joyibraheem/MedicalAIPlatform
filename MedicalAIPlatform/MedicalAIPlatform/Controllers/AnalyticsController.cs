using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services;
using MedicalAIPlatform.Services.Dicom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
public sealed class AnalyticsController : Controller
{
    private sealed record ChexNetResolveOutcome(
        Dictionary<string, CheXNetPredictionResponse> Map,
        List<string> PipelineNotes,
        PatientMedicalHistory? DicomMetadata = null);

    private const long MaxAnalyticsUploadBytes = 512L * 1024 * 1024;

    private readonly CheXNetApiClient _cheXNetApi;
    private readonly BioBertApiClient _bioBertApi;
    private readonly AnalyticsStateService _stateService;
    private readonly AnalyticsSessionLoader _sessionLoader;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DicomInferencePipelineOrchestrator _dicomPipeline;
    private readonly AnalyticsCtInferenceExecutor _ctInference;
    private readonly TrainingAssetPersistenceService _trainingAssets;
    private readonly ILogger<AnalyticsController> _logger;

    private static readonly DicomAggregationMethod DefaultSliceAggregation = DicomAggregationMethod.MaxPooling;

    public AnalyticsController(
        CheXNetApiClient cheXNetApi,
        BioBertApiClient bioBertApi,
        AnalyticsStateService stateService,
        AnalyticsSessionLoader sessionLoader,
        UserManager<ApplicationUser> userManager,
        DicomInferencePipelineOrchestrator dicomPipeline,
        AnalyticsCtInferenceExecutor ctInference,
        TrainingAssetPersistenceService trainingAssets,
        ILogger<AnalyticsController> logger)
    {
        _cheXNetApi = cheXNetApi;
        _bioBertApi = bioBertApi;
        _stateService = stateService;
        _sessionLoader = sessionLoader;
        _userManager = userManager;
        _dicomPipeline = dicomPipeline;
        _ctInference = ctInference;
        _trainingAssets = trainingAssets;
        _logger = logger;
    }

    private async Task SetDoctorName()
    {
        if (User?.Identity?.IsAuthenticated ?? false)
        {
            var user = await _userManager.GetUserAsync(User);
            ViewBag.DoctorName = user != null
                ? (user.FullName ?? user.UserName)
                : User.Identity?.Name ?? "Doctor";
        }
        else
        {
            ViewBag.DoctorName = "Doctor";
        }
    }

    public async Task<IActionResult> Index()
    {
        await SetDoctorName();
        return View();
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyticsUploadBytes)]
    [RequestSizeLimit(MaxAnalyticsUploadBytes)]
    public async Task<IActionResult> AnalyzeXRay(IFormFile xrayFile, CancellationToken cancellationToken)
    {
        if (xrayFile == null || xrayFile.Length == 0)
            return Json(new { success = false, error = "Please upload an X-Ray image." });

        try
        {
            LogUpload("AnalyzeXRay", xrayFile);
            var bytes = await ReadAllBytesAsync(xrayFile, cancellationToken).ConfigureAwait(false);
            var outcome = await ResolveCheXNetPredictionAsync(xrayFile, bytes, cancellationToken).ConfigureAwait(false);
            var results = outcome.Map;

            string? previewUrl = null;
            if (!AnalyticsDicomRouting.IsDicomUpload(xrayFile))
                previewUrl = MakeDataUrl(bytes, xrayFile.ContentType);
            else if (results.TryGetValue("CheXNet", out var chex) && !string.IsNullOrEmpty(chex.PreviewImageDataUrl))
                previewUrl = chex.PreviewImageDataUrl;

            _stateService.SetResults(
                results,
                null,
                null,
                previewUrl,
                null,
                null,
                outcome.PipelineNotes,
                cheXNetSourceJson: (await _trainingAssets.PersistCheXNetAsync(
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                    bytes,
                    SafeFileName(xrayFile),
                    xrayFile.ContentType ?? "",
                    outcome.DicomMetadata,
                    cancellationToken).ConfigureAwait(false)).ToJson());

            return Json(new { success = true, redirect = Url.Action("XRay") });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeXRay failed for {FileName}", SafeFileName(xrayFile));
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeText(string clinicalText)
    {
        if (string.IsNullOrWhiteSpace(clinicalText))
            return Json(new { success = false, error = "Please enter clinical text." });

        try
        {
            var results = await _bioBertApi.PredictAsync(clinicalText);
            var bioSource = await _trainingAssets.PersistBioBertAsync(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                clinicalText,
                ct: HttpContext.RequestAborted).ConfigureAwait(false);
            _stateService.SetResults(null, results, null, null, null, clinicalText, bioBertSourceJson: bioSource.ToJson());
            return Json(new { success = true, redirect = Url.Action("Text") });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyticsUploadBytes)]
    [RequestSizeLimit(MaxAnalyticsUploadBytes)]
    public async Task<IActionResult> AnalyzeCT(IFormFile ctFile, CancellationToken cancellationToken)
    {
        if (ctFile == null || ctFile.Length == 0)
            return Json(new { success = false, error = "Please upload a CT scan image." });

        try
        {
            LogUpload("AnalyzeCT", ctFile);
            var bytes = await ReadAllBytesAsync(ctFile, cancellationToken).ConfigureAwait(false);
            LungAICtResponse results = await ResolveCtPredictionAsync(ctFile, bytes, cancellationToken)
                .ConfigureAwait(false);

            string? ctPreviewUrl = null;
            if (!AnalyticsDicomRouting.IsDicomUpload(ctFile))
                ctPreviewUrl = MakeDataUrl(bytes, ctFile.ContentType);
            else if (!string.IsNullOrEmpty(results.PreviewImageDataUrl))
                ctPreviewUrl = results.PreviewImageDataUrl;

            _stateService.SetResults(
                null,
                null,
                results,
                null,
                ctPreviewUrl,
                null,
                lungCancerSourceJson: (await _trainingAssets.PersistLungCancerAsync(
                    User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                    bytes,
                    SafeFileName(ctFile),
                    ctFile.ContentType ?? "",
                    ct: cancellationToken).ConfigureAwait(false)).ToJson());
            return Json(new { success = true, redirect = Url.Action("CT") });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeCT failed for {FileName}", SafeFileName(ctFile));
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAnalyticsUploadBytes)]
    [RequestSizeLimit(MaxAnalyticsUploadBytes)]
    public async Task<IActionResult> AnalyzeCombined(
        [FromForm(Name = "xrayFile")] IFormFile? xrayFile,
        [FromForm(Name = "clinicalText")] string? clinicalText,
        [FromForm(Name = "ctFile")] IFormFile? ctFile,
        CancellationToken cancellationToken)
    {
        clinicalText = string.IsNullOrWhiteSpace(clinicalText) ? null : clinicalText.Trim();
        try
        {
            Task<ChexNetResolveOutcome>? chexTask = null;
            Task<BioBertResponse>? bertTask = null;

            byte[]? xRayBytes = null;
            byte[]? ctBytes = null;

            if (xrayFile is { Length: > 0 })
            {
                LogUpload("AnalyzeCombined:X-Ray slot", xrayFile);
                xRayBytes = await ReadAllBytesAsync(xrayFile, cancellationToken).ConfigureAwait(false);
                chexTask = ResolveCheXNetPredictionAsync(xrayFile, xRayBytes, cancellationToken);
            }

            if (clinicalText != null)
                bertTask = _bioBertApi.PredictAsync(clinicalText, cancellationToken);

            if (ctFile is { Length: > 0 })
            {
                LogUpload("AnalyzeCombined:CT slot", ctFile);
                ctBytes = await ReadAllBytesAsync(ctFile, cancellationToken).ConfigureAwait(false);
            }

            if (chexTask == null && bertTask == null && ctBytes == null)
                return Json(new { success = false, error = "Please provide at least one input." });

            // BioBERT runs in parallel with imaging. Both DICOM HTTP pipelines MUST NOT run concurrently:
            // they share the scoped DicomInferencePipelineOrchestrator and fo-dicom/renderer state — parallel
            // RunAsync causes races and random failures only when both X-Ray + CT DICOM slots are filled.
            Dictionary<string, CheXNetPredictionResponse>? chexResults = null;
            ChexNetResolveOutcome? chexOutcome = null;
            Exception? chexFault = null;
            LungAICtResponse? lungRaw = null;
            Exception? lungFault = null;

            async Task RunDicomBranchesOneAfterAnotherAsync()
            {
                if (chexTask is not null)
                {
                    (chexOutcome, chexFault) = await ConsumeTaskMaybe(chexTask).ConfigureAwait(false);
                    chexResults = chexOutcome?.Map;
                }

                // Start CT DICOM pipeline only after chest branch finishes — avoids overlapping RunAsync on scoped pipeline.
                if (ctBytes is not null && ctFile is { Length: > 0 })
                {
                    var ctPipe = ResolveCtPredictionAsync(ctFile, ctBytes, cancellationToken);
                    (lungRaw, lungFault) = await ConsumeTaskMaybe(ctPipe).ConfigureAwait(false);
                }
            }

            BioBertResponse? bioResults = null;
            Exception? bertFault = null;

            async Task RunBioBranchAsync()
            {
                if (bertTask is null)
                    return;
                (bioResults, bertFault) = await ConsumeTaskMaybe(bertTask).ConfigureAwait(false);
            }

            await Task.WhenAll(RunDicomBranchesOneAfterAnotherAsync(), RunBioBranchAsync()).ConfigureAwait(false);

            var warnings = new List<string>();
            if (chexFault is not null)
            {
                _logger.LogError(chexFault, "AnalyzeCombined: X-Ray branch failed");
                warnings.Add($"X-Ray / chest: {chexFault.Message}");
            }

            if (bertFault is not null)
            {
                _logger.LogError(bertFault, "AnalyzeCombined: BioBERT branch failed");
                warnings.Add($"Clinical text (BioBERT): {bertFault.Message}");
            }

            LungAICtResponse? lungResults = lungRaw;
            if (lungFault is not null)
            {
                _logger.LogError(lungFault, "AnalyzeCombined: CT / lung branch failed");
                warnings.Add($"CT / ChestAI: {lungFault.Message}");
                lungResults = new LungAICtResponse
                {
                    Probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    Error = lungFault.Message,
                };
            }

            int okBranches =
                (chexTask is not null && chexFault is null && chexResults?.ContainsKey("CheXNet") == true ? 1 : 0)
                + (bertTask is not null && bertFault is null && bioResults is not null ? 1 : 0)
                + (ctBytes is not null && lungFault is null ? 1 : 0);

            if (okBranches == 0)
                return Json(new
                {
                    success = false,
                    error = warnings.Count > 0
                        ? string.Join(" ", warnings)
                        : "Combined analysis could not finish. Check that inference services are running.",
                });

            string? xrayDataUrl = null;
            if (xRayBytes != null && xrayFile != null && !AnalyticsDicomRouting.IsDicomUpload(xrayFile))
                xrayDataUrl = MakeDataUrl(xRayBytes, xrayFile.ContentType);
            else if (
                chexResults != null
                && chexResults.TryGetValue("CheXNet", out var chexVm)
                && !string.IsNullOrEmpty(chexVm.PreviewImageDataUrl))
                xrayDataUrl = chexVm.PreviewImageDataUrl;

            string? ctDataUrl = null;
            if (ctBytes != null && ctFile != null && !AnalyticsDicomRouting.IsDicomUpload(ctFile))
                ctDataUrl = MakeDataUrl(ctBytes, ctFile.ContentType);
            else if (lungResults != null && !string.IsNullOrEmpty(lungResults.PreviewImageDataUrl))
                ctDataUrl = lungResults.PreviewImageDataUrl;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            string? chexSourceJson = null;
            string? bioSourceJson = null;
            string? lungSourceJson = null;
            if (xRayBytes != null && xrayFile != null)
            {
                chexSourceJson = (await _trainingAssets.PersistCheXNetAsync(
                    userId, xRayBytes, SafeFileName(xrayFile), xrayFile.ContentType ?? "",
                    chexOutcome?.DicomMetadata, cancellationToken).ConfigureAwait(false)).ToJson();
            }
            if (clinicalText != null)
            {
                bioSourceJson = (await _trainingAssets.PersistBioBertAsync(userId, clinicalText,
                    ct: cancellationToken).ConfigureAwait(false)).ToJson();
            }
            if (ctBytes != null && ctFile != null)
            {
                lungSourceJson = (await _trainingAssets.PersistLungCancerAsync(
                    userId, ctBytes, SafeFileName(ctFile), ctFile.ContentType ?? "",
                    ct: cancellationToken).ConfigureAwait(false)).ToJson();
            }

            _stateService.SetResults(
                chexResults,
                bioResults,
                lungResults,
                xrayDataUrl,
                ctDataUrl,
                clinicalText,
                chexOutcome?.PipelineNotes ?? [],
                chexSourceJson,
                lungSourceJson,
                bioSourceJson);

            if (warnings.Count > 0)
                TempData["CombinedWarnings"] = string.Join(" • ", warnings);

            return Json(new { success = true, redirect = Url.Action("Combined") ?? "/Analytics/Combined" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AnalyzeCombined failed");
            return Json(new { success = false, error = ex.Message });
        }
    }

    private static Task<(T? Result, Exception? Fault)> ConsumeTaskMaybe<T>(Task<T>? task) where T : class
        => task is null ? Task.FromResult<(T?, Exception?)>((null, null)) : AwaitQuiet(task);

    private static async Task<(T?, Exception?)> AwaitQuiet<T>(Task<T> task) where T : class
    {
        try
        {
            var v = await task.ConfigureAwait(false);
            return (v, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    public async Task<IActionResult> XRay(Guid? jobId)
    {
        if (!await TryPrepareResultPageAsync(jobId, snap =>
                snap.CurrentResults?.ContainsKey("CheXNet") == true))
        {
            TempData["Error"] = jobId.HasValue
                ? "This X-Ray result is unavailable or belongs to another session. Please re-run the analysis."
                : "No X-Ray analysis results available. Please analyze an X-Ray image first.";
            return RedirectToAction("Index");
        }

        await SetDoctorName();
        return View();
    }

    public async Task<IActionResult> Text(Guid? jobId)
    {
        if (!await TryPrepareResultPageAsync(jobId, snap => snap.CurrentBioBertResults is not null))
        {
            TempData["Error"] = jobId.HasValue
                ? "This text analysis result is unavailable or belongs to another session. Please re-run the analysis."
                : "No text analysis results available. Please analyze clinical text first.";
            return RedirectToAction("Index");
        }

        await SetDoctorName();
        return View();
    }

    public async Task<IActionResult> CT(Guid? jobId)
    {
        if (!await TryPrepareResultPageAsync(jobId, snap => snap.CurrentLungAIResults is not null))
        {
            TempData["Error"] = jobId.HasValue
                ? "This CT result is unavailable or belongs to another session. Please re-run the analysis."
                : "No CT scan analysis results available. Please analyze a CT scan first.";
            return RedirectToAction("Index");
        }

        await SetDoctorName();
        return View();
    }

    public async Task<IActionResult> Combined(Guid? jobId)
    {
        if (!await TryPrepareResultPageAsync(jobId, snap =>
                snap.CurrentResults is not null
                || snap.CurrentBioBertResults is not null
                || snap.CurrentLungAIResults is not null))
        {
            TempData["Error"] = jobId.HasValue
                ? "This combined result is unavailable or belongs to another session. Please re-run the analysis."
                : "No analysis results available. Please run an analysis first.";
            return RedirectToAction("Index");
        }

        await SetDoctorName();
        return View();
    }

    private async Task<bool> TryPrepareResultPageAsync(Guid? jobId,
        Func<AnalyticsSessionSnapshot, bool> isValid)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return false;

        var snapshot = await _sessionLoader.LoadAsync(userId, jobId, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (snapshot is null || !isValid(snapshot))
            return false;

        _stateService.HydrateForRequest(snapshot);
        return true;
    }

    private void LogUpload(string context, IFormFile file)
    {
        _logger.LogInformation(
            "[{Context}] Upload: file={File}, length={Len}, declaredContentType={ContentType}, isDicom={IsDicom}",
            context,
            SafeFileName(file),
            file.Length,
            string.IsNullOrEmpty(file.ContentType) ? "(empty)" : file.ContentType,
            AnalyticsDicomRouting.IsDicomUpload(file));
    }

    private static string SafeFileName(IFormFile file) =>
        string.IsNullOrEmpty(file.FileName) ? "(no name)" : Path.GetFileName(file.FileName);

    private async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream(capacity: (int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>CheXNet path: slice-based pipeline for DICOM, otherwise direct HTTP PNG/JPEG.</summary>
    private async Task<ChexNetResolveOutcome> ResolveCheXNetPredictionAsync(
        IFormFile fileMeta,
        byte[] fileBytes,
        CancellationToken cancellationToken)
    {
        if (AnalyticsDicomRouting.IsDicomUpload(fileMeta))
        {
            _logger.LogInformation(
                "Processing DICOM chest upload → slice-based pipeline (CheXNet per slice); file={File}",
                SafeFileName(fileMeta));

            await using var dicomMs = new MemoryStream(fileBytes, writable: false);
            var pipe = await _dicomPipeline
                .RunAsync(dicomMs, DefaultSliceAggregation, DicomInferenceBackend.HttpCheXNetChest, cancellationToken)
                .ConfigureAwait(false);

            var viewModel = AnalyticsDicomRouting.ToCheXNetPrediction(pipe);
            var notes = pipe.Metadata?.StudyNotes is { Count: > 0 } list
                ? new List<string>(list)
                : new List<string>();
            var map = new Dictionary<string, CheXNetPredictionResponse>(StringComparer.OrdinalIgnoreCase)
            {
                ["CheXNet"] = viewModel,
            };
            return new ChexNetResolveOutcome(map, notes, pipe.Metadata?.PatientMedicalHistory);
        }

        _logger.LogInformation(
            "Processing raster chest image → CheXNet HTTP endpoint; file={File}",
            SafeFileName(fileMeta));

        var rasterMap = await _cheXNetApi.PredictAsync(
            fileBytes,
            SafeFileName(fileMeta),
            string.IsNullOrWhiteSpace(fileMeta.ContentType) ? "application/octet-stream" : fileMeta.ContentType,
            models: "CheXNet",
            heatmapClass: null,
            topK: 14,
            cancellationToken).ConfigureAwait(false);

        return new ChexNetResolveOutcome(rasterMap, []);
    }

    /// <summary>CT path: slice pipeline for volumetric DICOM, otherwise LungAI JPEG/PNG endpoint.</summary>
    private Task<LungAICtResponse> ResolveCtPredictionAsync(
        IFormFile fileMeta,
        byte[] fileBytes,
        CancellationToken cancellationToken) =>
        _ctInference.PredictCtAsync(
            fileBytes,
            SafeFileName(fileMeta),
            string.IsNullOrWhiteSpace(fileMeta.ContentType) ? "application/octet-stream" : fileMeta.ContentType.Trim(),
            cancellationToken);

    private static string MakeDataUrl(ReadOnlySpan<byte> bytes, string? contentType)
    {
        var mime = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType.Trim();
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
