namespace MedicalAIPlatform.Models
{
    public class FollowUpViewModel
    {
        public int ComplianceRate { get; set; }
        public int HighUrgencyCases { get; set; }
        public int ScheduledToday { get; set; }
        public List<FollowUpPatient> Patients { get; set; } = new List<FollowUpPatient>();
    }

    public class FollowUpPatient
    {
        public string Name { get; set; } = "";
        public string PID { get; set; } = ""; // Patient ID e.g. #22940
        public string UrgencyLevel { get; set; } = "Low"; // High, Medium, Low
    }
}
