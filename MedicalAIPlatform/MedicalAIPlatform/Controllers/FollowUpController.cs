using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers
{
    [Authorize(Policy = "VerifiedMedicalUser")]
    public class FollowUpController : Controller
    {
        public IActionResult Index()
        {
            // Mock Data matching the user's image design
            var model = new FollowUpViewModel
            {
                ComplianceRate = 96,
                HighUrgencyCases = 12,
                ScheduledToday = 8,
                Patients = new List<FollowUpPatient>
                {
                    new FollowUpPatient { Name = "Sarah Jenkins", PID = "#22940", UrgencyLevel = "High" },
                    new FollowUpPatient { Name = "Michael Chen", PID = "#31184", UrgencyLevel = "Medium" },
                    new FollowUpPatient { Name = "Emma Wilson", PID = "#44201", UrgencyLevel = "Low" },
                    new FollowUpPatient { Name = "James Rodriquez", PID = "#19283", UrgencyLevel = "High" }
                }
            };

            return View(model);
        }
    }
}
