namespace MedicalAIPlatform.Options;

public sealed class TrainingCenterOptions
{
    public const string SectionName = "TrainingCenter";

    /// <summary>Override CheXNet-master folder. Empty = auto-detect next to web project.</summary>
    public string CheXNetMasterPath { get; set; } = "";

    /// <summary>Folder for uploaded dataset ZIP archives.</summary>
    public string DatasetUploadRoot { get; set; } = "TrainingData/dataset_uploads";

    /// <summary>Runtime JSON + logs for background BRAX jobs.</summary>
    public string JobStateRoot { get; set; } = "TrainingData/training_center_jobs";

    public int LiveRefreshSeconds { get; set; } = 5;
}
