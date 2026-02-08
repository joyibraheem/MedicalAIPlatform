using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers
{
    [Authorize(Roles = "Admin,Doctor")]
    public class PatientCalendarController : Controller
    {
        public IActionResult Index()
        {
            // Mock Data
            var appointments = new List<AppointmentViewModel>
            {
                new AppointmentViewModel { Id = 1, PatientName = "John Doe", AppointmentTime = DateTime.Now.AddHours(2), Type = "Checkup", Status = "Scheduled" },
                new AppointmentViewModel { Id = 2, PatientName = "Jane Smith", AppointmentTime = DateTime.Now.AddHours(4), Type = "Follow-up", Status = "Scheduled" },
                new AppointmentViewModel { Id = 3, PatientName = "Michael Scott", AppointmentTime = DateTime.Now.AddDays(1).AddHours(10), Type = "Consultation", Status = "Confirmed" },
                new AppointmentViewModel { Id = 4, PatientName = "Sarah Connor", AppointmentTime = DateTime.Now.AddDays(2).AddHours(14), Type = "Emergency", Status = "Pending" }
            };

            return View(appointments);
        }
    }
}
