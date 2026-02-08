using System;

namespace MedicalAIPlatform.Models
{
    public class AppointmentViewModel
    {
        public int Id { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public DateTime AppointmentTime { get; set; }
        public string Type { get; set; } = "Checkup"; // Checkup, Follow-up, Emergency
        public string Status { get; set; } = "Scheduled"; // Scheduled, Completed, Cancelled
    }
}
