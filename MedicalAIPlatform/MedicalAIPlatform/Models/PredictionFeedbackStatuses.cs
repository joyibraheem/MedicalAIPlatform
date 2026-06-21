namespace MedicalAIPlatform.Models;

public static class PredictionFeedbackStatuses
{
    public const string DoctorAccept = "Accept";
    public const string DoctorModify = "Modify";

    public const string ReviewPending = "Pending";
    public const string ReviewApproved = "Approved";
    public const string ReviewRejected = "Rejected";

    public const string AuditSubmitted = "Submitted";
    public const string AuditApproved = "Approved";
    public const string AuditRejected = "Rejected";
    public const string AuditExportMarked = "ExportMarked";
    public const string AuditUpdated = "Updated";
}
