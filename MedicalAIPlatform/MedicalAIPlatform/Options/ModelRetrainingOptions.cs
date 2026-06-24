namespace MedicalAIPlatform.Options;

using MedicalAIPlatform.Models;

public sealed class ModelRetrainingOptions
{
    public const string SectionName = "ModelRetraining";

    /// <summary>Default when per-model threshold is not set.</summary>
    public int ModifiedThreshold { get; set; } = 30;

    public int CheXNetModifiedThreshold { get; set; } = 30;

    public int LungCancerModifiedThreshold { get; set; } = 30;

    public int BioBERTModifiedThreshold { get; set; } = 30;

    /// <summary>Durable storage for original uploads linked to HITL samples.</summary>
    public string TrainingAssetsRoot { get; set; } = "TrainingAssets";

    /// <summary>Root folder for exported retraining datasets (JSON batches).</summary>
    public string DatasetExportRoot { get; set; } = "TrainingData";

    /// <summary>FastAPI training endpoint base URL (CheXNet API hosts /api/training/start).</summary>
    public string TrainingApiBaseUrl { get; set; } = "http://localhost:8000/";

    public bool EnableAutoRetraining { get; set; } = true;

    public int GetThreshold(string modelName) => modelName switch
    {
        ModelTrainingNames.CheXNet => CheXNetModifiedThreshold > 0 ? CheXNetModifiedThreshold : ModifiedThreshold,
        ModelTrainingNames.LungCancer => LungCancerModifiedThreshold > 0 ? LungCancerModifiedThreshold : ModifiedThreshold,
        ModelTrainingNames.BioBERT => BioBERTModifiedThreshold > 0 ? BioBERTModifiedThreshold : ModifiedThreshold,
        _ => ModifiedThreshold
    };
}
