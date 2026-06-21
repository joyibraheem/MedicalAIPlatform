using System.Globalization;
using System.Text;
using MedicalAIPlatform.Data;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;
using MedicalAIPlatform.Services.Dicom;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace MedicalAIPlatform.Services;

public sealed class CtScanViewerService
{
    private static readonly TimeSpan SessionMaxAge = TimeSpan.FromHours(4);

    private readonly ApplicationDbContext _db;
    private readonly ICtScanViewerSessionStore _store;
    private readonly IDicomDatasetLoaderService _dicomLoader;
    private readonly DicomMetadataParser _metadataParser;
    private readonly DicomSliceExtractionService _sliceExtractor;
    private readonly AnalyticsCtInferenceExecutor _ctInference;
    private readonly DicomPipelineOptions _pipelineOptions;
    private readonly ILogger<CtScanViewerService> _logger;

    public CtScanViewerService(
        ApplicationDbContext db,
        ICtScanViewerSessionStore store,
        IDicomDatasetLoaderService dicomLoader,
        DicomMetadataParser metadataParser,
        DicomSliceExtractionService sliceExtractor,
        AnalyticsCtInferenceExecutor ctInference,
        IOptions<DicomPipelineOptions> pipelineOptions,
        ILogger<CtScanViewerService> logger)
    {
        _db = db;
        _store = store;
        _dicomLoader = dicomLoader;
        _metadataParser = metadataParser;
        _sliceExtractor = sliceExtractor;
        _ctInference = ctInference;
        _pipelineOptions = pipelineOptions.Value;
        _logger = logger;
    }

    public async Task<CtScanViewerSession> CreateFromUploadAsync(
        string userId,
        byte[] bytes,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        _store.PurgeExpired(SessionMaxAge);
        return await BuildSessionAsync(userId, bytes, fileName, contentType, patientScanId: null, patientId: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CtScanViewerSession?> CreateFromPatientScanAsync(
        string userId,
        int scanId,
        CancellationToken cancellationToken)
    {
        _store.PurgeExpired(SessionMaxAge);

        var scan = await _db.PatientScans
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scanId, cancellationToken)
            .ConfigureAwait(false);

        if (scan?.ImageData is not { Length: > 0 })
            return null;

        return await BuildSessionAsync(
                userId,
                scan.ImageData,
                scan.FileName ?? $"scan-{scan.Id}.dcm",
                scan.ContentType ?? "application/octet-stream",
                scan.Id,
                scan.PatientId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public CtScanViewerSession? GetSession(Guid sessionId, string userId) =>
        _store.Get(sessionId, userId);

    public CtScanSessionSummaryDto ToSummary(CtScanViewerSession session) =>
        new()
        {
            SessionId = session.SessionId,
            FileName = session.FileName,
            SliceCount = session.Slices.Count,
            Metadata = session.Metadata,
            Slices = session.Slices,
            HasAnalysis = session.Analysis is not null,
            PatientScanId = session.PatientScanId,
            PatientId = session.PatientId,
        };

    public string? GetSliceFilePath(CtScanViewerSession session, int sliceIndex)
    {
        if (sliceIndex < 0 || sliceIndex >= session.Slices.Count)
            return null;

        var fileName = session.Slices[sliceIndex].JpegFileName;
        var path = Path.Combine(session.TempDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    public async Task<CtScanAnalysisPanelDto> AnalyzeAsync(
        CtScanViewerSession session,
        CancellationToken cancellationToken)
    {
        var safeName = string.IsNullOrWhiteSpace(session.FileName) ? "ct-upload.dcm" : Path.GetFileName(session.FileName);
        var result = await _ctInference
            .PredictCtAsync(session.SourceBytes, safeName, session.ContentType, cancellationToken)
            .ConfigureAwait(false);

        session.Analysis = result;
        _store.Save(session);

        return CtScanRiskMapper.ToPanel(result);
    }

    public byte[] BuildReportBytes(CtScanViewerSession session)
    {
        var sb = new StringBuilder();
        var meta = session.Metadata;
        sb.AppendLine("Medical AI Platform — CT Scan Analysis Report");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Source file: {session.FileName}");
        sb.AppendLine($"Slices: {session.Slices.Count}");
        sb.AppendLine();

        sb.AppendLine("Study metadata");
        sb.AppendLine(new string('-', 40));
        AppendMeta(sb, "Patient", meta.PatientName);
        AppendMeta(sb, "Patient ID", meta.PatientId);
        AppendMeta(sb, "Modality", meta.Modality);
        AppendMeta(sb, "Body part", meta.BodyPartExamined);
        AppendMeta(sb, "Study description", meta.StudyDescription);
        AppendMeta(sb, "Series description", meta.SeriesDescription);
        if (meta.StudyDateTime is not null)
            AppendMeta(sb, "Study date", meta.StudyDateTime.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        sb.AppendLine();
        sb.AppendLine("AI analysis");
        sb.AppendLine(new string('-', 40));

        if (session.Analysis is null)
        {
            sb.AppendLine("No analysis has been run. Use Analyze Scan in the CT Viewer first.");
        }
        else if (!string.IsNullOrWhiteSpace(session.Analysis.Error))
        {
            sb.AppendLine($"Error: {session.Analysis.Error}");
        }
        else
        {
            var panel = CtScanRiskMapper.ToPanel(session.Analysis);
            sb.AppendLine($"Model: {panel.ModelName}");
            sb.AppendLine($"Predicted disease / class: {panel.PredictedDisease}");
            sb.AppendLine($"Confidence score: {panel.ConfidenceScore:P2}");
            sb.AppendLine($"Risk level: {panel.RiskLevel}");
            sb.AppendLine();
            sb.AppendLine("Class probabilities:");
            foreach (var kv in panel.Probabilities.OrderByDescending(x => x.Value))
                sb.AppendLine($"  • {kv.Key}: {kv.Value:P2}");
        }

        sb.AppendLine();
        sb.AppendLine("Disclaimer: AI-assisted output for clinical decision support only. Not a definitive diagnosis.");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private async Task<CtScanViewerSession> BuildSessionAsync(
        string userId,
        byte[] bytes,
        string fileName,
        string contentType,
        int? patientScanId,
        int? patientId,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var tempDir = Path.Combine(Path.GetTempPath(), "MedicalAiPlatform", "ct-viewer", sessionId.ToString("N"));
        Directory.CreateDirectory(tempDir);

        PatientMedicalHistory metadata;
        List<CtScanSliceInfo> slices;

        if (AnalyticsDicomRouting.IsDicomUpload(fileName, contentType))
        {
            await using var ms = new MemoryStream(bytes, writable: false);
            var dicomFile = await _dicomLoader.LoadAsync(ms, cancellationToken).ConfigureAwait(false);
            metadata = _metadataParser.Parse(dicomFile);
            slices = await ExtractDicomSlicesAsync(dicomFile, metadata, tempDir, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            metadata = new PatientMedicalHistory
            {
                Modality = "CT",
                BodyPartExamined = "CHEST",
                NumberOfFrames = 1,
            };
            slices = [await SaveRasterSliceAsync(bytes, tempDir, cancellationToken).ConfigureAwait(false)];
        }

        var session = new CtScanViewerSession
        {
            SessionId = sessionId,
            UserId = userId,
            FileName = fileName,
            ContentType = contentType,
            SourceBytes = bytes,
            Metadata = metadata,
            Slices = slices,
            TempDirectory = tempDir,
            PatientScanId = patientScanId,
            PatientId = patientId,
        };

        _store.Save(session);
        _logger.LogInformation(
            "CT viewer session {SessionId} created with {SliceCount} slice(s) for user {UserId}",
            sessionId, slices.Count, userId);

        return session;
    }

    private async Task<List<CtScanSliceInfo>> ExtractDicomSlicesAsync(
        FellowOakDicom.DicomFile dicomFile,
        PatientMedicalHistory metadata,
        string tempDir,
        CancellationToken cancellationToken)
    {
        var slices = new List<CtScanSliceInfo>();
        var total = Math.Max(1, metadata.NumberOfFrames);
        var bodyPart = string.IsNullOrWhiteSpace(metadata.BodyPartExamined) ? "CHEST" : metadata.BodyPartExamined.ToUpperInvariant();

        foreach (var frame in _sliceExtractor.EnumerateSlices(dicomFile, metadata, cancellationToken))
        {
            using (frame)
            {
                var jpegName = $"{frame.SliceIndex:D4}.jpg";
                var jpegPath = Path.Combine(tempDir, jpegName);
                await SaveRgbAsJpegAsync(frame.Pixels, jpegPath, cancellationToken).ConfigureAwait(false);

                slices.Add(new CtScanSliceInfo
                {
                    Index = frame.SliceIndex,
                    InstanceNumber = frame.InstanceNumber,
                    Label = $"CT {frame.SliceIndex + 1}/{total} {bodyPart}",
                    Width = frame.Pixels.Width,
                    Height = frame.Pixels.Height,
                    JpegFileName = jpegName,
                });
            }
        }

        if (slices.Count == 0)
            throw new InvalidOperationException("No slices could be extracted from this DICOM file.");

        return slices;
    }

    private async Task<CtScanSliceInfo> SaveRasterSliceAsync(
        byte[] bytes,
        string tempDir,
        CancellationToken cancellationToken)
    {
        const string jpegName = "0000.jpg";
        var jpegPath = Path.Combine(tempDir, jpegName);

        using var image = Image.Load<Rgb24>(bytes);
        await SaveRgbAsJpegAsync(image, jpegPath, cancellationToken).ConfigureAwait(false);

        return new CtScanSliceInfo
        {
            Index = 0,
            Label = "CT 1/1 CHEST",
            Width = image.Width,
            Height = image.Height,
            JpegFileName = jpegName,
        };
    }

    private Task SaveRgbAsJpegAsync(Image<Rgb24> image, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var quality = (byte)Math.Clamp(_pipelineOptions.ExportJpegQuality, 70, 100);
        using var clone = image.Clone();
        clone.SaveAsJpeg(path, new JpegEncoder { Quality = quality });
        return Task.CompletedTask;
    }

    private static void AppendMeta(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        sb.AppendLine($"{label}: {value.Trim()}");
    }
}
