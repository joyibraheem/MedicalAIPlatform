using System.Security.Claims;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

[Authorize(Policy = "VerifiedMedicalUser")]
public sealed class CtScanController : Controller
{
    private const long MaxUploadBytes = 512L * 1024 * 1024;

    private readonly CtScanViewerService _viewer;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CtScanController> _logger;

    public CtScanController(
        CtScanViewerService viewer,
        UserManager<ApplicationUser> userManager,
        ILogger<CtScanController> logger)
    {
        _viewer = viewer;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(Guid? sessionId, int? scanId)
    {
        ViewData["Title"] = "CT Scan";
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Challenge();

        if (sessionId is null && scanId is not null)
        {
            var fromScan = await _viewer.CreateFromPatientScanAsync(userId, scanId.Value, HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (fromScan is null)
                return NotFound();

            return RedirectToAction(nameof(Index), new { sessionId = fromScan.SessionId });
        }

        CtScanViewerSession? session = null;
        if (sessionId is not null)
            session = _viewer.GetSession(sessionId.Value, userId);

        ViewBag.SessionId = session?.SessionId;
        ViewBag.HasSession = session is not null;
        ViewBag.FileName = session?.FileName;
        ViewBag.SliceCount = session?.Slices.Count ?? 0;
        ViewBag.PatientScanId = session?.PatientScanId;
        ViewBag.PatientId = session?.PatientId;

        var user = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        ViewBag.DoctorName = user?.FullName ?? user?.UserName ?? User.Identity?.Name ?? "Doctor";

        return View();
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [RequestSizeLimit(MaxUploadBytes)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile dicomFile, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { success = false, error = "Not signed in." });

        if (dicomFile is null || dicomFile.Length == 0)
            return BadRequest(new { success = false, error = "Please choose a DICOM or image file." });

        try
        {
            await using var ms = new MemoryStream(capacity: (int)Math.Min(dicomFile.Length, int.MaxValue));
            await dicomFile.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            var safeName = string.IsNullOrWhiteSpace(dicomFile.FileName)
                ? "upload.dcm"
                : Path.GetFileName(dicomFile.FileName);
            var contentType = string.IsNullOrWhiteSpace(dicomFile.ContentType)
                ? "application/octet-stream"
                : dicomFile.ContentType;

            var session = await _viewer
                .CreateFromUploadAsync(userId, bytes, safeName, contentType, cancellationToken)
                .ConfigureAwait(false);

            return Json(new
            {
                success = true,
                sessionId = session.SessionId,
                redirect = Url.Action(nameof(Index), new { sessionId = session.SessionId }),
                summary = _viewer.ToSummary(session),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CT viewer upload failed for {FileName}", dicomFile.FileName);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Session(Guid sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var session = _viewer.GetSession(sessionId, userId);
        if (session is null)
            return NotFound();

        CtScanAnalysisPanelDto? analysis = session.Analysis is not null
            ? CtScanRiskMapper.ToPanel(session.Analysis)
            : null;

        return Json(new
        {
            summary = _viewer.ToSummary(session),
            analysis,
        });
    }

    [HttpGet]
    public IActionResult Slice(Guid sessionId, int index)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var session = _viewer.GetSession(sessionId, userId);
        if (session is null)
            return NotFound();

        var path = _viewer.GetSliceFilePath(session, index);
        if (path is null)
            return NotFound();

        return PhysicalFile(path, "image/jpeg");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Analyze(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { success = false, error = "Not signed in." });

        var session = _viewer.GetSession(sessionId, userId);
        if (session is null)
            return NotFound(new { success = false, error = "Viewer session not found or expired." });

        try
        {
            var panel = await _viewer.AnalyzeAsync(session, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(panel.Error))
            {
                return Json(new
                {
                    success = false,
                    error = panel.Error,
                    analysis = panel,
                });
            }

            return Json(new { success = true, analysis = panel });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CT viewer analyze failed for session {SessionId}", sessionId);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult DownloadReport(Guid sessionId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var session = _viewer.GetSession(sessionId, userId);
        if (session is null)
            return NotFound();

        var bytes = _viewer.BuildReportBytes(session);
        var baseName = Path.GetFileNameWithoutExtension(session.FileName);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "ct-scan";

        return File(bytes, "text/plain; charset=utf-8", $"{baseName}-analysis-report.txt");
    }
}
