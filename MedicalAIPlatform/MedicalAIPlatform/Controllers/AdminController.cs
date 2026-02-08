using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // [Route("Dashboard")] -- Removed to unify under DashboardController
        public async Task<IActionResult> Index()
        {
            // Fetch stats
            var activeDoctors = await _userManager.GetUsersInRoleAsync("Doctor");
            var allUsers = await _userManager.Users.ToListAsync();
            // Assuming non-doctors/non-admins are patients for this count, or check against patient role if exists
            // For simplicity, let's just count users who aren't doctors or admins
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            
            var patientsCount = allUsers.Count - activeDoctors.Count - admins.Count; 
            if (patientsCount < 0) patientsCount = 0; // Safety

            // Get pending doctors
            var pendingDoctors = await _userManager.Users
                .Where(u => u.DoctorStatus == "Pending")
                .OrderByDescending(u => u.CreatedAt) // Most recent first
                .Take(5)
                .ToListAsync();

            var model = new AdminDashboardViewModel
            {
                ActiveDoctorsCount = activeDoctors.Count,
                PatientsCount = patientsCount, // Or fetch strictly if "Patient" role exists
                AccuracyRate = 98.4, // Mocked as per design
                PendingDoctors = pendingDoctors,
                IsSystemWorking = true
            };

            return View(model);
        }

        public async Task<IActionResult> PendingDoctors()
        {
            var pendingDoctors = await _userManager.Users
                .Where(u => u.DoctorStatus == "Pending")
                .ToListAsync();
            return View(pendingDoctors);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveDoctor(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.DoctorStatus == "Pending")
            {
                user.DoctorStatus = "Verified";
                await _userManager.UpdateAsync(user);
                await _userManager.AddToRoleAsync(user, "Doctor");
            }
            return RedirectToAction(nameof(PendingDoctors));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectDoctor(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user != null && user.DoctorStatus == "Pending")
            {
                user.DoctorStatus = "Rejected";
                await _userManager.UpdateAsync(user);
                // Optionally delete user or keep as rejected
            }
            return RedirectToAction(nameof(PendingDoctors));
        }
    }
}
