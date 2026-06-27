using MedicalAIPlatform.Models.TrainingCenter;

namespace MedicalAIPlatform.Services.TrainingCenter;

public interface IModelTrainingPipeline
{
    string ModelId { get; }
    string DisplayName { get; }
    string Description { get; }
    string PipelineKind { get; }

    Task<ModelManagementCardDto> BuildCardAsync(CancellationToken ct = default);
    TrainingConfigDefaultsDto GetConfigDefaults();
    Task<DatasetValidationResultDto> ValidateDatasetAsync(string datasetPath, CancellationToken ct = default);
    Task<TrainingConfirmationDto> BuildConfirmationAsync(TrainingStartRequestDto request, CancellationToken ct = default);
    Task<TrainingStartResponseDto> StartTrainingAsync(TrainingStartRequestDto request, string userId, CancellationToken ct = default);
}
