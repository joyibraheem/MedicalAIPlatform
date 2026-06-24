using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services;

/// <summary>Persists uploaded source files for human-in-the-loop retraining (durable disk paths).</summary>
public sealed class TrainingAssetPersistenceService
{
    private readonly ModelRetrainingOptions _options;
    private readonly ILogger<TrainingAssetPersistenceService> _logger;

    public TrainingAssetPersistenceService(
        IOptions<ModelRetrainingOptions> options,
        ILogger<TrainingAssetPersistenceService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TrainingSourceData> PersistCheXNetAsync(
        string userId,
        byte[] bytes,
        string fileName,
        string contentType,
        PatientMedicalHistory? dicomMeta = null,
        CancellationToken ct = default)
    {
        var assetId = Guid.NewGuid();
        var dir = CreateAssetDirectory(ModelTrainingNames.CheXNet, userId, assetId);
        var isDicom = AnalyticsDicomRouting.IsDicomUpload(fileName, contentType);
        string? dicomPath = null;
        string imagePath;

        if (isDicom)
        {
            dicomPath = Path.Combine(dir, SanitizeFileName(fileName, "study.dcm"));
            await File.WriteAllBytesAsync(dicomPath, bytes, ct).ConfigureAwait(false);
            imagePath = Path.Combine(dir, "training_slice.jpg");
            await WritePreviewFromBytesAsync(bytes, imagePath, ct).ConfigureAwait(false);
        }
        else
        {
            imagePath = Path.Combine(dir, SanitizeFileName(fileName, "xray.jpg"));
            await File.WriteAllBytesAsync(imagePath, bytes, ct).ConfigureAwait(false);
        }

        return new TrainingSourceData
        {
            ModelKey = ModelTrainingNames.CheXNet,
            ImagePath = imagePath,
            DicomPath = dicomPath,
            StudyInstanceUid = NullIfEmpty(dicomMeta?.StudyInstanceUid),
            SeriesInstanceUid = ReadSeriesUid(dicomMeta),
            SliceImagePaths = string.IsNullOrEmpty(imagePath) ? [] : [imagePath]
        };
    }

    public async Task<TrainingSourceData> PersistLungCancerAsync(
        string userId,
        byte[] bytes,
        string fileName,
        string contentType,
        PatientMedicalHistory? dicomMeta = null,
        IReadOnlyList<string>? slicePaths = null,
        CancellationToken ct = default)
    {
        var assetId = Guid.NewGuid();
        var dir = CreateAssetDirectory(ModelTrainingNames.LungCancer, userId, assetId);
        var isDicom = AnalyticsDicomRouting.IsDicomUpload(fileName, contentType);
        string? dicomPath = null;
        string imagePath;

        if (isDicom)
        {
            dicomPath = Path.Combine(dir, SanitizeFileName(fileName, "ct_study.dcm"));
            await File.WriteAllBytesAsync(dicomPath, bytes, ct).ConfigureAwait(false);
            imagePath = Path.Combine(dir, "training_slice.jpg");
            await WritePreviewFromBytesAsync(bytes, imagePath, ct).ConfigureAwait(false);
        }
        else
        {
            imagePath = Path.Combine(dir, SanitizeFileName(fileName, "ct.jpg"));
            await File.WriteAllBytesAsync(imagePath, bytes, ct).ConfigureAwait(false);
        }

        var slices = slicePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList()
                     ?? [imagePath];

        return new TrainingSourceData
        {
            ModelKey = ModelTrainingNames.LungCancer,
            ImagePath = imagePath,
            DicomPath = dicomPath,
            StudyInstanceUid = NullIfEmpty(dicomMeta?.StudyInstanceUid),
            SeriesInstanceUid = ReadSeriesUid(dicomMeta),
            SliceImagePaths = slices
        };
    }

    public async Task<TrainingSourceData> PersistBioBertAsync(
        string userId,
        string clinicalText,
        string? originalReport = null,
        CancellationToken ct = default)
    {
        var assetId = Guid.NewGuid();
        var dir = CreateAssetDirectory(ModelTrainingNames.BioBERT, userId, assetId);
        var textPath = Path.Combine(dir, "clinical.txt");
        await File.WriteAllTextAsync(textPath, clinicalText, ct).ConfigureAwait(false);

        string? reportPath = null;
        if (!string.IsNullOrWhiteSpace(originalReport))
        {
            reportPath = Path.Combine(dir, "report.txt");
            await File.WriteAllTextAsync(reportPath, originalReport, ct).ConfigureAwait(false);
        }

        return new TrainingSourceData
        {
            ModelKey = ModelTrainingNames.BioBERT,
            ImagePath = textPath,
            OriginalClinicalText = clinicalText,
            OriginalReport = originalReport,
            SliceImagePaths = []
        };
    }

    public async Task<TrainingSourceData> CopyJobAssetAsync(
        string modelName,
        string userId,
        Guid jobId,
        string tempPath,
        string fileName,
        string contentType,
        CancellationToken ct = default)
    {
        if (!File.Exists(tempPath))
            throw new FileNotFoundException("Job temp file missing.", tempPath);

        var bytes = await File.ReadAllBytesAsync(tempPath, ct).ConfigureAwait(false);
        var source = modelName switch
        {
            ModelTrainingNames.CheXNet => await PersistCheXNetAsync(userId, bytes, fileName, contentType, null, ct)
                .ConfigureAwait(false),
            ModelTrainingNames.LungCancer => await PersistLungCancerAsync(userId, bytes, fileName, contentType, null, null, ct)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(modelName))
        };
        source.RelatedJobId = jobId;
        return source;
    }

    private string CreateAssetDirectory(string modelName, string userId, Guid assetId)
    {
        var root = Path.GetFullPath(_options.TrainingAssetsRoot);
        var dir = Path.Combine(root, modelName, userId, assetId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string SanitizeFileName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;
        var file = Path.GetFileName(name);
        return string.IsNullOrWhiteSpace(file) ? fallback : file;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ReadSeriesUid(PatientMedicalHistory? meta)
    {
        if (meta is null)
            return null;
        if (meta.AdditionalTags.TryGetValue("series_instance_uid", out var uid))
            return NullIfEmpty(uid);
        return null;
    }

    private async Task WritePreviewFromBytesAsync(byte[] bytes, string outputPath, CancellationToken ct)
    {
        try
        {
            await File.WriteAllBytesAsync(outputPath, bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write preview bytes to {Path}", outputPath);
        }
    }
}
