using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalAIPlatform.Services;

/// <summary>Seeds baseline production model versions on first run.</summary>
public static class ModelVersionBootstrap
{
    public static async Task EnsureSeedAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        if (await db.ModelVersions.AnyAsync(ct).ConfigureAwait(false))
            return;

        var now = DateTimeOffset.UtcNow;
        var seeds = new[]
        {
            new ModelVersion
            {
                Id = Guid.NewGuid(),
                ModelName = ModelTrainingNames.CheXNet,
                VersionNumber = "1.0",
                TrainingDate = now,
                DatasetSize = 0,
                Accuracy = 0.84,
                F1Score = 0.81,
                Loss = null,
                FilePath = "model.pth.tar",
                IsProduction = true,
                IsDeployable = false
            },
            new ModelVersion
            {
                Id = Guid.NewGuid(),
                ModelName = ModelTrainingNames.LungCancer,
                VersionNumber = "1.0",
                TrainingDate = now,
                DatasetSize = 0,
                Accuracy = 0.79,
                F1Score = 0.76,
                Loss = null,
                FilePath = "lung_cancer_detection_model.pth",
                IsProduction = true,
                IsDeployable = false
            },
            new ModelVersion
            {
                Id = Guid.NewGuid(),
                ModelName = ModelTrainingNames.BioBERT,
                VersionNumber = "1.0",
                TrainingDate = now,
                DatasetSize = 0,
                Accuracy = 0.88,
                F1Score = 0.85,
                Loss = null,
                FilePath = "biobert-base",
                IsProduction = true,
                IsDeployable = false
            }
        };

        db.ModelVersions.AddRange(seeds);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
