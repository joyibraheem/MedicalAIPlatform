using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers
{
    [Authorize(Policy = "VerifiedMedicalUser")]
    public class PatientController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly CheXNetApiClient _cheXNetApi;
        private readonly BioBertApiClient _bioBertApi;
        private readonly LungAIApiClient _lungAIApi;
        private readonly ScanAiAnalysisPipelineService _analysisPipeline;

        public PatientController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            CheXNetApiClient cheXNetApi,
            BioBertApiClient bioBertApi,
            LungAIApiClient lungAIApi,
            ScanAiAnalysisPipelineService analysisPipeline)
        {
            _context = context;
            _userManager = userManager;
            _cheXNetApi = cheXNetApi;
            _bioBertApi = bioBertApi;
            _lungAIApi = lungAIApi;
            _analysisPipeline = analysisPipeline;
        }

        // GET: Patient — full list; search filters rows in the browser (no query-string navigation).
        public async Task<IActionResult> Index()
        {
            var patients = await _context.Patients.AsQueryable()
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PatientViewModel
                {
                    Id = p.Id,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    FullName = p.FirstName + " " + p.LastName,
                    DateOfBirth = p.DateOfBirth,
                    Gender = p.Gender,
                    PatientId = p.PatientId,
                    Email = p.Email,
                    PhoneNumber = p.PhoneNumber,
                    CreatedAt = p.CreatedAt,
                    ProfileImageDataUrl = p.ProfileImageData != null
                        ? $"data:{p.ProfileImageContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(p.ProfileImageData)}"
                        : null
                })
                .ToListAsync();

            return View(patients);
        }

        /// <summary>JSON search for the Select Patient flow (server-side filter; min 2 characters).</summary>
        [HttpGet]
        public async Task<IActionResult> SearchPatients(string? q, CancellationToken cancellationToken = default)
        {
            q = q?.Trim() ?? "";
            if (q.Length < 2)
                return Json(Array.Empty<object>());

            var matches = await _context.Patients.AsQueryable()
                .Where(p =>
                    p.FirstName.Contains(q) ||
                    p.LastName.Contains(q) ||
                    (p.PatientId != null && p.PatientId.Contains(q)) ||
                    (p.Email != null && p.Email.Contains(q)))
                .OrderByDescending(p => p.CreatedAt)
                .Take(100)
                .Select(p => new { p.Id, p.FirstName, p.LastName, p.PatientId, p.DateOfBirth })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var rows = matches.Select(p => new
            {
                id = p.Id,
                fullName = $"{p.FirstName} {p.LastName}",
                patientId = p.PatientId ?? "",
                dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"),
                initials =
                    $"{(string.IsNullOrEmpty(p.FirstName) ? "?" : p.FirstName[0])}{(string.IsNullOrEmpty(p.LastName) ? "?" : p.LastName[0])}"
            }).ToList();

            return Json(rows);
        }

        // GET: Patient/Create - Form to create new patient
        public IActionResult Create()
        {
            return View(new PatientFormViewModel());
        }

        // POST: Patient/Create - Save new patient with history and images
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PatientFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            try
            {
                // Create patient
                var patient = new Patient
                {
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    DateOfBirth = model.DateOfBirth,
                    Gender = model.Gender,
                    PhoneNumber = model.PhoneNumber,
                    Email = model.Email,
                    Address = model.Address,
                    PatientId = model.PatientId ?? GeneratePatientId(),
                    MedicalHistorySummary = model.MedicalHistorySummary,
                    Allergies = model.Allergies,
                    CurrentMedications = model.CurrentMedications,
                    CreatedByUserId = user.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                // Handle profile image upload
                if (model.ProfileImage != null && model.ProfileImage.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await model.ProfileImage.CopyToAsync(ms);
                    patient.ProfileImageData = ms.ToArray();
                    patient.ProfileImageContentType = model.ProfileImage.ContentType;
                }

                _context.Patients.Add(patient);
                await _context.SaveChangesAsync();

                // Create initial history entry if provided
                if (!string.IsNullOrWhiteSpace(model.ConditionDescription) ||
                    !string.IsNullOrWhiteSpace(model.ClinicalNotes) ||
                    !string.IsNullOrWhiteSpace(model.Diagnosis))
                {
                    var historyEntry = new PatientHistory
                    {
                        PatientId = patient.Id,
                        VisitDate = model.VisitDate ?? DateTime.UtcNow,
                        VisitType = model.VisitType ?? "Initial Consultation",
                        ChiefComplaint = model.ChiefComplaint,
                        ClinicalNotes = model.ClinicalNotes,
                        Diagnosis = model.Diagnosis,
                        TreatmentPlan = model.TreatmentPlan,
                        ConditionDescription = model.ConditionDescription,
                        CreatedByUserId = user.Id,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.PatientHistories.Add(historyEntry);
                    await _context.SaveChangesAsync();

                    // Handle X-ray upload
                    if (model.XRayImage != null && model.XRayImage.Length > 0)
                    {
                        await SavePatientScan(patient.Id, historyEntry.Id, model.XRayImage, "XRay", user.Id);
                    }

                    // Handle CT scan upload
                    if (model.CTScanImage != null && model.CTScanImage.Length > 0)
                    {
                        await SavePatientScan(patient.Id, historyEntry.Id, model.CTScanImage, "CTScan", user.Id);
                    }
                }
                else
                {
                    // Save scans even without history entry
                    if (model.XRayImage != null && model.XRayImage.Length > 0)
                    {
                        await SavePatientScan(patient.Id, null, model.XRayImage, "XRay", user.Id);
                    }

                    if (model.CTScanImage != null && model.CTScanImage.Length > 0)
                    {
                        await SavePatientScan(patient.Id, null, model.CTScanImage, "CTScan", user.Id);
                    }
                }

                TempData["SuccessMessage"] = $"Patient {patient.FullName} created successfully.";
                return RedirectToAction(nameof(Details), new { id = patient.Id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error creating patient: {ex.Message}");
                return View(model);
            }
        }

        // GET: Patient/Details/{id} - View patient details with images and history
        public async Task<IActionResult> Details(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.HistoryEntries)
                .Include(p => p.Scans)
                    .ThenInclude(s => s.AiAnalysis!)
                        .ThenInclude(a => a.MedicalReport)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (patient == null)
            {
                return NotFound();
            }

            // Order collections after loading
            var orderedHistory = patient.HistoryEntries.OrderByDescending(h => h.VisitDate).ToList();
            var orderedScans = patient.Scans.OrderByDescending(s => s.ScanDate).ToList();

            var viewModel = new PatientDetailsViewModel
            {
                Patient = patient,
                ProfileImageDataUrl = patient.ProfileImageData != null
                    ? $"data:{patient.ProfileImageContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(patient.ProfileImageData)}"
                    : null,
                HistoryEntries = orderedHistory,
                Scans = orderedScans.Select(s => new PatientScanDetailViewModel
                {
                    Id = s.Id,
                    ScanType = s.ScanType,
                    ScanDate = s.ScanDate,
                    FileName = s.FileName,
                    ImageDataUrl = s.ImageDataUrl ?? (s.ImageData != null
                        ? $"data:{s.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(s.ImageData)}"
                        : null),
                    ScanAiAnalysisId = s.AiAnalysis?.Id,
                    AnalysisStatus = s.AiAnalysis?.Status ?? ScanAiAnalysisStatuses.Pending,
                    LinkedModels = s.AiAnalysis?.LinkedModels,
                    GeneratedResult = s.AiAnalysis?.GeneratedResult,
                    ResultGeneratedAt = s.AiAnalysis?.ResultGeneratedAt,
                    MedicalReportId = s.AiAnalysis?.MedicalReport?.Id
                }).ToList()
            };

            return View(viewModel);
        }

        // GET: Patient/Select - Patient selection/search page
        public IActionResult Select()
        {
            return View();
        }

        // POST: Patient/UploadScan - Upload X-ray or CT scan for existing patient
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadScan(int patientId, IFormFile scanFile, string scanType, string? conditionText = null)
        {
            if (scanFile == null || scanFile.Length == 0)
            {
                return Json(new { success = false, error = "Please upload a scan file." });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, error = "Unauthorized." });
            }

            try
            {
                int? historyId = null;

                // Create history entry if condition text is provided
                if (!string.IsNullOrWhiteSpace(conditionText))
                {
                    var historyEntry = new PatientHistory
                    {
                        PatientId = patientId,
                        VisitDate = DateTime.UtcNow,
                        VisitType = "Scan Upload",
                        ConditionDescription = conditionText,
                        CreatedByUserId = user.Id,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.PatientHistories.Add(historyEntry);
                    await _context.SaveChangesAsync();
                    historyId = historyEntry.Id;
                }

                await SavePatientScan(patientId, historyId, scanFile, scanType, user.Id);

                return Json(new { success = true, message = "Scan uploaded successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // POST: Patient/LinkModels - Link AI models to a patient scan and generate results
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LinkModels(int patientId, int scanId, string models, string? clinicalText = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Json(new { success = false, error = "Unauthorized." });
            }

            try
            {
                var scan = await _context.PatientScans
                    .Include(s => s.Patient)
                    .Include(s => s.PatientHistory)
                    .Include(s => s.AiAnalysis)
                    .FirstOrDefaultAsync(s => s.Id == scanId && s.PatientId == patientId);

                if (scan == null)
                {
                    return Json(new { success = false, error = "Scan not found." });
                }

                var linkedModels = new List<string>();
                string? chexnetJson = null;
                string? biobertJson = null;
                string? lungaiJson = null;

                // Parse models from JSON string
                string[] modelsArray = Array.Empty<string>();
                if (!string.IsNullOrWhiteSpace(models))
                {
                    try
                    {
                        modelsArray = JsonSerializer.Deserialize<string[]>(models) ?? Array.Empty<string>();
                    }
                    catch
                    {
                        // If JSON parsing fails, try comma-separated string
                        modelsArray = models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    }
                }

                // Process each selected model
                foreach (var model in modelsArray)
                {
                    if (model.Equals("CheXNet", StringComparison.OrdinalIgnoreCase) && scan.ScanType == "XRay")
                    {
                        if (scan.ImageData != null)
                        {
                            var results = await _cheXNetApi.PredictAsync(
                                imageBytes: scan.ImageData,
                                fileName: scan.FileName ?? "scan.jpg",
                                contentType: scan.ContentType ?? "image/jpeg",
                                models: "CheXNet",
                                topK: 14);

                            if (results != null && results.ContainsKey("CheXNet"))
                            {
                                chexnetJson = JsonSerializer.Serialize(results["CheXNet"]);
                                linkedModels.Add("CheXNet");
                            }
                        }
                    }
                    else if ((model.Equals(ChestXRayModels.BraxRaddino, StringComparison.OrdinalIgnoreCase)
                              || model.Equals("BRAX", StringComparison.OrdinalIgnoreCase)
                              || model.Equals("RADDINO", StringComparison.OrdinalIgnoreCase))
                             && scan.ScanType == "XRay")
                    {
                        if (scan.ImageData != null)
                        {
                            var results = await _cheXNetApi.PredictRaddinoAsync(
                                imageBytes: scan.ImageData,
                                fileName: scan.FileName ?? "scan.jpg",
                                contentType: scan.ContentType ?? "image/jpeg");

                            if (results.TryGetValue(ChestXRayModels.BraxRaddino, out var brax))
                            {
                                chexnetJson = JsonSerializer.Serialize(brax);
                                linkedModels.Add(ChestXRayModels.BraxRaddino);
                            }
                        }
                    }
                    else if (model.Equals("BioBert", StringComparison.OrdinalIgnoreCase))
                    {
                        var textToAnalyze = clinicalText ?? scan.PatientHistory?.ConditionDescription ?? "";
                        if (!string.IsNullOrWhiteSpace(textToAnalyze))
                        {
                            var results = await _bioBertApi.PredictAsync(textToAnalyze);
                            if (results != null)
                            {
                                biobertJson = JsonSerializer.Serialize(results);
                                linkedModels.Add("BioBert");
                            }
                        }
                    }
                    else if (model.Equals("LungAI", StringComparison.OrdinalIgnoreCase) && scan.ScanType == "CTScan")
                    {
                        if (scan.ImageData != null)
                        {
                            var results = await _lungAIApi.PredictCtAsync(
                                scan.ImageData,
                                scan.FileName ?? "ct.jpg",
                                scan.ContentType ?? "image/jpeg");

                            if (results != null)
                            {
                                lungaiJson = JsonSerializer.Serialize(results);
                                linkedModels.Add("LungAI");
                            }
                        }
                    }
                }

                var analysis = scan.AiAnalysis
                    ?? await _analysisPipeline.GetOrCreateForScanAsync(scan.Id, user.Id);

                await _analysisPipeline.ApplyModelResultsAsync(
                    analysis,
                    chexnetJson,
                    biobertJson,
                    lungaiJson,
                    linkedModels,
                    GenerateResultSummary(chexnetJson, biobertJson, lungaiJson));

                return Json(new
                {
                    success = true,
                    message = "Models linked successfully.",
                    linkedModels = linkedModels,
                    generatedResult = analysis.GeneratedResult,
                    analysisStatus = analysis.Status
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // GET: Patient/GetPatientData - API endpoint to retrieve patient data
        [HttpGet]
        public async Task<IActionResult> GetPatientData(int id)
        {
            var patient = await _context.Patients
                .Include(p => p.HistoryEntries)
                .Include(p => p.Scans)
                    .ThenInclude(s => s.AiAnalysis!)
                        .ThenInclude(a => a.MedicalReport)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (patient == null)
            {
                return Json(new { success = false, error = "Patient not found." });
            }

            // Order collections after loading
            var orderedHistory = patient.HistoryEntries.OrderByDescending(h => h.VisitDate).ToList();
            var orderedScans = patient.Scans.OrderByDescending(s => s.ScanDate).ToList();

            var data = new
            {
                success = true,
                patient = new
                {
                    id = patient.Id,
                    firstName = patient.FirstName,
                    lastName = patient.LastName,
                    fullName = patient.FirstName + " " + patient.LastName,
                    dateOfBirth = patient.DateOfBirth,
                    gender = patient.Gender,
                    patientId = patient.PatientId,
                    profileImageDataUrl = patient.ProfileImageData != null
                        ? $"data:{patient.ProfileImageContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(patient.ProfileImageData)}"
                        : null
                },
                scans = orderedScans.Select(s => new
                {
                    id = s.Id,
                    scanType = s.ScanType,
                    scanDate = s.ScanDate,
                    fileName = s.FileName,
                    imageDataUrl = s.ImageDataUrl ?? (s.ImageData != null
                        ? $"data:{s.ContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(s.ImageData)}"
                        : null),
                    scanAiAnalysisId = s.AiAnalysis?.Id,
                    analysisStatus = s.AiAnalysis?.Status ?? ScanAiAnalysisStatuses.Pending,
                    linkedModels = s.AiAnalysis?.LinkedModels,
                    chexnetResults = s.AiAnalysis?.CheXNetResults != null
                        ? JsonSerializer.Deserialize<object>(s.AiAnalysis.CheXNetResults)
                        : null,
                    biobertResults = s.AiAnalysis?.BioBertResults != null
                        ? JsonSerializer.Deserialize<object>(s.AiAnalysis.BioBertResults)
                        : null,
                    lungaiResults = s.AiAnalysis?.LungAIResults != null
                        ? JsonSerializer.Deserialize<object>(s.AiAnalysis.LungAIResults)
                        : null,
                    generatedResult = s.AiAnalysis?.GeneratedResult,
                    medicalReportId = s.AiAnalysis?.MedicalReport?.Id
                }).ToList(),
                history = orderedHistory.Select(h => new
                {
                    id = h.Id,
                    visitDate = h.VisitDate,
                    visitType = h.VisitType,
                    conditionDescription = h.ConditionDescription,
                    clinicalNotes = h.ClinicalNotes,
                    diagnosis = h.Diagnosis
                }).ToList()
            };

            return Json(data);
        }

        // Helper method to save patient scan
        private async Task SavePatientScan(int patientId, int? historyId, IFormFile file, string scanType, string userId)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var imageBytes = ms.ToArray();
            var imageDataUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(imageBytes)}";

            var scan = new PatientScan
            {
                PatientId = patientId,
                PatientHistoryId = historyId,
                ScanType = scanType,
                ScanDate = DateTime.UtcNow,
                FileName = file.FileName,
                ContentType = file.ContentType,
                ImageData = imageBytes,
                ImageDataUrl = imageDataUrl,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                AiAnalysis = new ScanAiAnalysis
                {
                    Status = ScanAiAnalysisStatuses.Pending,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow
                }
            };

            _context.PatientScans.Add(scan);
            await _context.SaveChangesAsync();
        }

        // Helper method to generate patient ID
        private string GeneratePatientId()
        {
            var count = _context.Patients.Count() + 1;
            return $"PAT-{DateTime.UtcNow:yyyyMMdd}-{count:D5}";
        }

        // Helper method to generate result summary
        private string GenerateResultSummary(string? chexnetJson, string? biobertJson, string? lungaiJson)
        {
            var summaryParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(chexnetJson))
            {
                try
                {
                    var chexnet = JsonSerializer.Deserialize<CheXNetPredictionResponse>(chexnetJson);
                    if (chexnet != null && chexnet.TopK.Any())
                    {
                        var topFindings = string.Join(", ", chexnet.TopK.Take(3).Select(t => $"{t.ClassName} ({t.Probability:P1})"));
                        summaryParts.Add($"X-Ray Analysis: {topFindings}");
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(biobertJson))
            {
                try
                {
                    var biobert = JsonSerializer.Deserialize<BioBertResponse>(biobertJson);
                    if (biobert != null && biobert.Entities.Any())
                    {
                        var entities = string.Join(", ", biobert.Entities.Take(5).Select(e => e.Word));
                        summaryParts.Add($"Clinical Text Analysis: Detected entities - {entities}");
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(lungaiJson))
            {
                try
                {
                    var lungai = JsonSerializer.Deserialize<LungAICtResponse>(lungaiJson);
                    if (lungai != null && !string.IsNullOrWhiteSpace(lungai.PredictedClass))
                    {
                        summaryParts.Add($"CT Scan Analysis: Predicted class - {lungai.PredictedClass}");
                    }
                }
                catch { }
            }

            return string.Join("\n\n", summaryParts);
        }
    }
}
