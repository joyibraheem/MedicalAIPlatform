using System;
using System.Collections.Generic;

namespace MedicalAIPlatform.Models
{
    public class AIAlertViewModel
    {
        public int Id { get; set; }
        public string PatientName { get; set; } = string.Empty;
        public string AlertType { get; set; } = string.Empty; // e.g., "Abnormal Lab Result"
        public string Severity { get; set; } = "Low"; // Low, Medium, High
        public string SuggestedNextStep { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
