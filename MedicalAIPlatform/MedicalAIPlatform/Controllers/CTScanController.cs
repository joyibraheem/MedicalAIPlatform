using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers;

public sealed class CTScanIdRequest
{
    public int Id { get; set; }
}

[Authorize]
public sealed class CTScanController : Controller
{
    private const long MaxUploadBytes = 512L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".dcm", ".dicm"];
    private const string UploadFolder = "uploads/ctscans";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CTScanController> _logger;

    public CTScanController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        ILogger<CTScanController> logger)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        if (User?.Identity?.IsAuthenticated ?? false)
        {
            var user = await _userManager.GetUserAsync(User);
            ViewBag.DoctorName = user?.FullName ?? user?.UserName ?? User.Identity?.Name ?? "Doctor";
        }
        else
        {
            ViewBag.DoctorName = "Doctor";
        }

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetScans(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        var scans = await _context.CTScans
            .AsNoTracking()
            .Where(s => s.CreatedByUserId == userId)
            .OrderBy(s => s.SeriesIndex)
            .ThenBy(s => s.UploadedAt)
            .Select(s => new
            {
                s.Id,
                imageUrl = s.ImagePath,
                label = $"CT {s.SeriesIndex}/{s.SeriesTotal} {s.BodyPart}",
                s.PredictionResult,
                s.Confidence,
                s.Notes
            })
            .ToListAsync(cancellationToken);

        return Json(new { success = true, scans });
    }

    [HttpPost]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload(IFormFile file, string? patientName, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, error = "Please select a file to upload." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return Json(new { success = false, error = "Only JPG, JPEG, PNG, and DCM files are supported." });

        try
        {
            var userId = _userManager.GetUserId(User);
            var existing = await _context.CTScans
                .Where(s => s.CreatedByUserId == userId)
                .OrderBy(s => s.SeriesIndex)
                .ToListAsync(cancellationToken);

            var seriesIndex = existing.Count + 1;
            var seriesTotal = seriesIndex;

            foreach (var scan in existing)
                scan.SeriesTotal = seriesTotal;

            var uploadDir = Path.Combine(_env.WebRootPath, UploadFolder);
            Directory.CreateDirectory(uploadDir);

            var safeName = $"{Guid.NewGuid():N}{ext}";
            var relativePath = $"/{UploadFolder}/{safeName}";
            var fullPath = Path.Combine(uploadDir, safeName);

            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(fs, cancellationToken);
            }

            var entity = new CTScan
            {
                PatientName = string.IsNullOrWhiteSpace(patientName) ? null : patientName.Trim(),
                ScanTitle = Path.GetFileNameWithoutExtension(file.FileName),
                ScanType = "CT",
                ImagePath = relativePath,
                ContentType = file.ContentType,
                FileName = file.FileName,
                UploadedAt = DateTime.UtcNow,
                Status = "Uploaded",
                SeriesIndex = seriesIndex,
                SeriesTotal = seriesTotal,
                BodyPart = "CHEST",
                CreatedByUserId = userId
            };

            _context.CTScans.Add(entity);
            await _context.SaveChangesAsync(cancellationToken);

            return Json(new
            {
                success = true,
                scan = new
                {
                    entity.Id,
                    imageUrl = entity.ImagePath,
                    label = $"CT {entity.SeriesIndex}/{entity.SeriesTotal} {entity.BodyPart}",
                    entity.PredictionResult,
                    entity.Confidence,
                    entity.Notes
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CT scan upload failed for {FileName}", file.FileName);
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Delete([FromBody] CTScanIdRequest request, CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);
        var scan = await _context.CTScans
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.CreatedByUserId == userId, cancellationToken);

        if (scan == null)
            return Json(new { success = false, error = "Scan not found." });

        try
        {
            if (!string.IsNullOrEmpty(scan.ImagePath))
            {
                var physicalPath = Path.Combine(
                    _env.WebRootPath,
                    scan.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (System.IO.File.Exists(physicalPath))
                    System.IO.File.Delete(physicalPath);
            }

            _context.CTScans.Remove(scan);
            await _context.SaveChangesAsync(cancellationToken);
            await RenumberSeriesAsync(userId, cancellationToken);

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete CT scan {ScanId}", request.Id);
            return Json(new { success = false, error = ex.Message });
        }
    }

    private async Task RenumberSeriesAsync(string? userId, CancellationToken cancellationToken)
    {
        var scans = await _context.CTScans
            .Where(s => s.CreatedByUserId == userId)
            .OrderBy(s => s.SeriesIndex)
            .ThenBy(s => s.UploadedAt)
            .ToListAsync(cancellationToken);

        var total = scans.Count;
        for (var i = 0; i < scans.Count; i++)
        {
            scans[i].SeriesIndex = i + 1;
            scans[i].SeriesTotal = total;
        }

        if (scans.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }
}
