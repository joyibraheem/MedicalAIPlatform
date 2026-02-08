using System.Collections.Generic;

namespace MedicalAIPlatform.Models
{
    public class AdminDashboardViewModel
    {
        public int ActiveDoctorsCount { get; set; }
        public int PatientsCount { get; set; }
        public double AccuracyRate { get; set; }
        public List<ApplicationUser> PendingDoctors { get; set; } = new List<ApplicationUser>();
        public bool IsSystemWorking { get; set; } = true;
    }
}
