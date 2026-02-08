using MedicalAIPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers
{
    [Authorize(Roles = "Admin,Doctor")]
    public class AIAlertsController : Controller
    {
        public IActionResult Index()
        {
            // Mock AI Logic / Data
            var alerts = new List<AIAlertViewModel>
            {
                new AIAlertViewModel 
                { 
                    Id = 1, 
                    PatientName = "Alice Johnson", 
                    AlertType = "Abnormal Lab Result", 
                    Severity = "High", 
                    SuggestedNextStep = "Review Blood Work & Call Patient", 
                    Timestamp = DateTime.Now.AddMinutes(-45) 
                },
                new AIAlertViewModel 
                { 
                    Id = 2, 
                    PatientName = "Bob Brown", 
                    AlertType = "Arrhythmia Detected", 
                    Severity = "Medium", 
                    SuggestedNextStep = "Schedule ECG Follow-up", 
                    Timestamp = DateTime.Now.AddHours(-3) 
                },
                new AIAlertViewModel 
                { 
                    Id = 3, 
                    PatientName = "Charlie Davis", 
                    AlertType = "Declining Vitals", 
                    Severity = "Low", 
                    SuggestedNextStep = "Monitor Closely", 
                    Timestamp = DateTime.Now.AddHours(-12) 
                }
            };

            return View(alerts);
        }
    }
}
