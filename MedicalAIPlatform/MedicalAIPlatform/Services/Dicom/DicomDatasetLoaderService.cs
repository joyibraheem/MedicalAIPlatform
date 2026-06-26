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
        // ReadAll: load metadata + pixel data while the stream is still open. ReadLargeOnDemand
        // defers pixel reads and breaks when callers pass a MemoryStream that is disposed after Open.
        return await DicomFile.OpenAsync(stream, FileReadOption.ReadAll).ConfigureAwait(false);
    }
}
