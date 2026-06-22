using MedicalAIPlatform.Models;
using MedicalAIPlatform.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdminDashboardService _adminDashboard;
    private readonly DoctorRegistrationService _registrations;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        AdminDashboardService adminDashboard,
        DoctorRegistrationService registrations)
    {
        _userManager = userManager;
        _adminDashboard = adminDashboard;
        _registrations = registrations;
    }

    public async Task<IActionResult> Index()
    {
        var model = await _adminDashboard.BuildAsync(CancellationToken.None).ConfigureAwait(false);
        return View(model);
    }

    public async Task<IActionResult> PendingDoctors()
    {
        var pendingDoctors = await _registrations.GetPendingSubmittedRequestsAsync(HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return View(pendingDoctors);
    }

    public async Task<IActionResult> DoctorDetails(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return RedirectToAction(nameof(PendingDoctors));

        var details = await _registrations.GetDetailsAsync(userId, HttpContext.RequestAborted).ConfigureAwait(false);
        if (details is null)
            return NotFound();

        return View(details);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveDoctor(string userId)
    {
        var admin = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (admin is null)
            return Unauthorized();

        var (ok, error) = await _registrations.ApproveAsync(userId, admin.Id, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!ok)
            TempData["AdminError"] = error ?? "Approval failed.";

        return RedirectToAction(nameof(DoctorDetails), new { userId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectDoctor(RejectDoctorViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["AdminError"] = "Rejection reason is required.";
            return RedirectToAction(nameof(DoctorDetails), new { userId = model.UserId });
        }

        var admin = await _userManager.GetUserAsync(User).ConfigureAwait(false);
        if (admin is null)
            return Unauthorized();

        var (ok, error) = await _registrations.RejectAsync(
                model.UserId, admin.Id, model.RejectionReason, HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (!ok)
            TempData["AdminError"] = error ?? "Rejection failed.";

        return RedirectToAction(nameof(DoctorDetails), new { userId = model.UserId });
    }

    public IActionResult PredictionFeedbackReview() => View();
}
