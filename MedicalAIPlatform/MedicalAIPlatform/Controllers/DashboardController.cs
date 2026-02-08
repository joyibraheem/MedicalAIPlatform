using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Controllers
{
    [Route("Dashboard")]
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public DashboardController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        [Route("")]
        [Route("Index")]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                 return RedirectToAction("Login", "Account");
            }

            // Check for Admin Role
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                // --- ADMIN DASHBOARD LOGIC ---
                var activeDoctors = await _userManager.GetUsersInRoleAsync("Doctor");
                var allUsers = await _userManager.Users.ToListAsync();
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                
                var patientsCount = allUsers.Count - activeDoctors.Count - admins.Count; 
                if (patientsCount < 0) patientsCount = 0; 

                var pendingDoctors = await _userManager.Users
                    .Where(u => u.DoctorStatus == "Pending")
                    .OrderByDescending(u => u.CreatedAt) 
                    .Take(5)
                    .ToListAsync();

                var adminModel = new AdminDashboardViewModel
                {
                    ActiveDoctorsCount = activeDoctors.Count,
                    PatientsCount = patientsCount, 
                    AccuracyRate = 98.4, 
                    PendingDoctors = pendingDoctors,
                    IsSystemWorking = true
                };

                // Return the Admin View
                return View("~/Views/Admin/Index.cshtml", adminModel);
            }

            // --- DOCTOR DASHBOARD LOGIC (Default) ---
            var model = new DashboardViewModel
            {
                IsAdmin = false, 
                IsDoctor = true,
                UserName = user.FullName ?? "Dr. Smith"
            };
            
            // Populate logic
            model.PendingAIReportsCount = 12;
            model.UrgentPatientAlertsCount = 03;
            model.UpcomingAppointmentsCount = 08;
            model.UpcomingAppointments = new List<AppointmentViewModel>
            {
                new AppointmentViewModel { Id = 1, PatientName = "John Doe", AppointmentTime = DateTime.Now.AddHours(2), Type = "checkup", Status = "Confirmed" },
                new AppointmentViewModel { Id = 2, PatientName = "Jane Smith", AppointmentTime = DateTime.Now.AddHours(4), Type = "Follow-up", Status = "Confirmed" }
            };
            model.RecentAlerts = new List<AIAlertViewModel>
            {
                new AIAlertViewModel { Id = 101, PatientName = "Alice Johnson", AlertType = "High Blood Pressure", Severity = "High", SuggestedNextStep = "Immediate Consultation", Timestamp = DateTime.Now.AddMinutes(-30) },
                new AIAlertViewModel { Id = 102, PatientName = "Bob Brown", AlertType = "Irregular Heartbeat", Severity = "Medium", SuggestedNextStep = "Schedule ECG", Timestamp = DateTime.Now.AddHours(-2) }
            };
            model.LatestScans = new List<PatientScanViewModel>
            {
                    new PatientScanViewModel { PatientName = "Sarah Miller", ScanId = "#8210-Ahhhh", Status = "Normal" },
                    new PatientScanViewModel { PatientName = "John Doe", ScanId = "#4492-C", Status = "High Risk" },
                    new PatientScanViewModel { PatientName = "Robert Chen", ScanId = "#9102-K", Status = "Normal" }
            };

            return View("Index", model); // Views/Dashboard/Index.cshtml
        }
    }
}
