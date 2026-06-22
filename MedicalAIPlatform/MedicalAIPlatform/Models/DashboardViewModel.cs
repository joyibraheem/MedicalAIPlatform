using System.Collections.Generic;
using System;

namespace MedicalAIPlatform.Models
{
    public class DashboardViewModel
    {
        // User Info
        public string UserName { get; set; } = "Doctor";

        // Admin Stats
        public int TotalUsers { get; set; }
        public int TotalDoctors { get; set; }
        public int TotalPatients { get; set; }

        // Doctor Stats (New Design)
        public int PendingAIReportsCount { get; set; }
        public int UrgentPatientAlertsCount { get; set; }

        // Doctor Content
        public List<AIAlertViewModel> RecentAlerts { get; set; } = new List<AIAlertViewModel>();
        
        public List<PatientScanViewModel> LatestScans { get; set; } = new List<PatientScanViewModel>();

        public bool IsAdmin { get; set; }
        public bool IsDoctor { get; set; }
    }

    public class PatientScanViewModel
    {
        public string PatientName { get; set; }
        public string ScanId { get; set; }
        public string Status { get; set; } // "Normal", "High Risk"
        public string ImageUrl { get; set; } // Placeholder
    }
}
