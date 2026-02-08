using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MedicalAIPlatform.Services;
using MedicalAIPlatform.Models;
using System.Text;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Identity;

namespace MedicalAIPlatform.Controllers
{
    [Authorize]
    public class AnalyticsController : Controller
    {
        private readonly CheXNetApiClient _cheXNetApi;
        private readonly BioBertApiClient _bioBertApi;
        private readonly LungAIApiClient _lungAIApi;
        private readonly AnalyticsStateService _stateService;
        private readonly UserManager<ApplicationUser> _userManager;

        public AnalyticsController(
            CheXNetApiClient cheXNetApi,
            BioBertApiClient bioBertApi,
            LungAIApiClient lungAIApi,
            AnalyticsStateService stateService,
            UserManager<ApplicationUser> userManager)
        {
            _cheXNetApi = cheXNetApi;
            _bioBertApi = bioBertApi;
            _lungAIApi = lungAIApi;
            _stateService = stateService;
            _userManager = userManager;
        }

        private async Task SetDoctorName()
        {
            if (User?.Identity?.IsAuthenticated ?? false)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    ViewBag.DoctorName = user.FullName ?? user.UserName;
                }
                else
                {
                    ViewBag.DoctorName = User.Identity.Name;
                }
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
        public async Task<IActionResult> AnalyzeXRay(IFormFile xrayFile)
        {
            if (xrayFile == null || xrayFile.Length == 0)
            {
                return Json(new { success = false, error = "Please upload an X-Ray image." });
            }

            try
            {
                using var ms = new MemoryStream();
                await xrayFile.CopyToAsync(ms);
                var imageBytes = ms.ToArray();
                var imageDataUrl = $"data:{xrayFile.ContentType};base64,{Convert.ToBase64String(imageBytes)}";

                var results = await _cheXNetApi.PredictAsync(
                    imageBytes: imageBytes,
                    fileName: xrayFile.FileName,
                    contentType: xrayFile.ContentType,
                    models: "CheXNet",
                    topK: 14);

                _stateService.SetResults(results, null, null, imageDataUrl, null, null);

                return Json(new { success = true, redirect = Url.Action("XRay") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeText(string clinicalText)
        {
            if (string.IsNullOrWhiteSpace(clinicalText))
            {
                return Json(new { success = false, error = "Please enter clinical text." });
            }

            try
            {
                var results = await _bioBertApi.PredictAsync(clinicalText);
                _stateService.SetResults(null, results, null, null, null, clinicalText);
                return Json(new { success = true, redirect = Url.Action("Text") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeCT(IFormFile ctFile)
        {
            if (ctFile == null || ctFile.Length == 0)
            {
                return Json(new { success = false, error = "Please upload a CT scan image." });
            }

            try
            {
                using var ms = new MemoryStream();
                await ctFile.CopyToAsync(ms);
                var imageBytes = ms.ToArray();
                var imageDataUrl = $"data:{ctFile.ContentType};base64,{Convert.ToBase64String(imageBytes)}";

                var results = await _lungAIApi.PredictCtAsync(imageBytes, ctFile.FileName, ctFile.ContentType);
                _stateService.SetResults(null, null, results, null, imageDataUrl, null);
                return Json(new { success = true, redirect = Url.Action("CT") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeCombined(IFormFile? xrayFile, string? clinicalText, IFormFile? ctFile)
        {
            try
            {
                Task<Dictionary<string, CheXNetPredictionResponse>>? chexTask = null;
                Task<BioBertResponse>? bertTask = null;
                Task<LungAICtResponse>? ctTask = null;

                string? xrayDataUrl = null;
                string? ctDataUrl = null;

                if (xrayFile != null && xrayFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await xrayFile.CopyToAsync(ms);
                    var imageBytes = ms.ToArray();
                    xrayDataUrl = $"data:{xrayFile.ContentType};base64,{Convert.ToBase64String(imageBytes)}";
                    chexTask = _cheXNetApi.PredictAsync(imageBytes, xrayFile.FileName, xrayFile.ContentType, "CheXNet", null, 14);
                }

                if (!string.IsNullOrWhiteSpace(clinicalText))
                {
                    bertTask = _bioBertApi.PredictAsync(clinicalText);
                }

                if (ctFile != null && ctFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await ctFile.CopyToAsync(ms);
                    var imageBytes = ms.ToArray();
                    ctDataUrl = $"data:{ctFile.ContentType};base64,{Convert.ToBase64String(imageBytes)}";
                    ctTask = _lungAIApi.PredictCtAsync(imageBytes, ctFile.FileName, ctFile.ContentType);
                }

                var toWait = new List<Task>();
                if (chexTask != null) toWait.Add(chexTask);
                if (bertTask != null) toWait.Add(bertTask);
                if (ctTask != null) toWait.Add(ctTask);

                if (toWait.Count == 0)
                {
                    return Json(new { success = false, error = "Please provide at least one input." });
                }

                await Task.WhenAll(toWait);

                Dictionary<string, CheXNetPredictionResponse>? chexResults = null;
                BioBertResponse? bioBertResults = null;
                LungAICtResponse? lungAIResults = null;

                if (chexTask != null) chexResults = await chexTask;
                if (bertTask != null) bioBertResults = await bertTask;
                if (ctTask != null) lungAIResults = await ctTask;

                _stateService.SetResults(chexResults, bioBertResults, lungAIResults, xrayDataUrl, ctDataUrl, clinicalText);
                return Json(new { success = true, redirect = Url.Action("Combined") });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        public async Task<IActionResult> XRay()
        {
            if (_stateService.CurrentResults == null || !_stateService.CurrentResults.ContainsKey("CheXNet"))
            {
                TempData["Error"] = "No X-Ray analysis results available. Please analyze an X-Ray image first.";
                return RedirectToAction("Index");
            }
            await SetDoctorName();
            return View();
        }

        public async Task<IActionResult> Text()
        {
            if (_stateService.CurrentBioBertResults == null)
            {
                TempData["Error"] = "No text analysis results available. Please analyze clinical text first.";
                return RedirectToAction("Index");
            }
            await SetDoctorName();
            return View();
        }

        public async Task<IActionResult> CT()
        {
            if (_stateService.CurrentLungAIResults == null)
            {
                TempData["Error"] = "No CT scan analysis results available. Please analyze a CT scan first.";
                return RedirectToAction("Index");
            }
            await SetDoctorName();
            return View();
        }

        public async Task<IActionResult> Combined()
        {
            if (_stateService.CurrentResults == null && 
                _stateService.CurrentBioBertResults == null && 
                _stateService.CurrentLungAIResults == null)
            {
                TempData["Error"] = "No analysis results available. Please run an analysis first.";
                return RedirectToAction("Index");
            }
            await SetDoctorName();
            return View();
        }
    }
}
