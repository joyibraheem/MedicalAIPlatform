using FellowOakDicom;

namespace MedicalAIPlatform.Services.Dicom;

public interface IDicomDatasetLoaderService
{
    ValueTask<DicomFile> LoadAsync(Stream stream, CancellationToken cancellationToken = default);
}

public sealed class DicomDatasetLoaderService : IDicomDatasetLoaderService
{
    private readonly ILogger<DicomDatasetLoaderService> _log;

    public DicomDatasetLoaderService(ILogger<DicomDatasetLoaderService> log) => _log = log;

    public async ValueTask<DicomFile> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        _log.LogInformation("Opening DICOM from stream ({Length} bytes if seekable)",
            stream.CanSeek ? stream.Length : null);
        return await DicomFile.OpenAsync(stream, FileReadOption.ReadLargeOnDemand).ConfigureAwait(false);
    }
}
