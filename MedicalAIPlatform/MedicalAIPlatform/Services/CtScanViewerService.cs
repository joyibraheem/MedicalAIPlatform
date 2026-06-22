using System.Globalization;
using System.IO.Compression;
using System.Text;
using FellowOakDicom;
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

    public CtScanSessionSummaryDto ToSummary(CtScanViewerSession session)
    {
        var seriesSummaries = session.Series.Select(s => new CtSeriesSummaryDto
        {
            SeriesIndex = s.SeriesIndex,
            Label = s.Label,
            Modality = s.Modality,
            SeriesDescription = s.SeriesDescription,
            SliceCount = s.Slices.Count,
            PreviewSliceIndex = s.PreviewSliceIndex,
            Slices = s.Slices,
        }).ToList();

        var totalSlices = session.Series.Sum(s => s.Slices.Count);
        var layoutMode = ResolveLayoutMode(session.Series);

        return new CtScanSessionSummaryDto
        {
            SessionId = session.SessionId,
            FileName = session.FileName,
            SliceCount = totalSlices,
            SeriesCount = session.Series.Count,
            LayoutMode = layoutMode,
            Metadata = session.Metadata,
            Series = seriesSummaries,
            HasAnalysis = session.Analysis is not null,
            PatientScanId = session.PatientScanId,
            PatientId = session.PatientId,
        };
    }

    public string? GetSliceFilePath(CtScanViewerSession session, int seriesIndex, int sliceIndex)
    {
        var series = session.Series.FirstOrDefault(s => s.SeriesIndex == seriesIndex)
                     ?? session.Series.ElementAtOrDefault(seriesIndex);
        if (series is null || sliceIndex < 0 || sliceIndex >= series.Slices.Count)
            return null;

        var fileName = series.Slices[sliceIndex].JpegFileName;
        var path = Path.Combine(session.TempDirectory, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Legacy — first series.</summary>
    public string? GetSliceFilePath(CtScanViewerSession session, int sliceIndex) =>
        GetSliceFilePath(session, 0, sliceIndex);

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
        sb.AppendLine($"Series: {session.Series.Count}");
        sb.AppendLine($"Total slices: {session.Series.Sum(s => s.Slices.Count)}");
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
        sb.AppendLine("Series in study");
        sb.AppendLine(new string('-', 40));
        foreach (var s in session.Series)
        {
            sb.AppendLine($"  [{s.SeriesIndex}] {s.Label} — {s.Slices.Count} slice(s), modality={s.Modality}");
        }

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

        IReadOnlyList<CtSeriesInfo> seriesList;
        PatientMedicalHistory metadata;

        if (IsZipUpload(fileName, contentType))
        {
            (seriesList, metadata) = await LoadSeriesFromZipAsync(bytes, tempDir, cancellationToken).ConfigureAwait(false);
        }
        else if (AnalyticsDicomRouting.IsDicomUpload(fileName, contentType))
        {
            var extracted = await ExtractSeriesFromDicomBytesAsync(bytes, seriesIndex: 0, tempDir, cancellationToken)
                .ConfigureAwait(false);
            seriesList = [extracted.Info];
            metadata = extracted.MetadataForStudy ?? new PatientMedicalHistory();
        }
        else
        {
            metadata = new PatientMedicalHistory
            {
                Modality = "CT",
                BodyPartExamined = "CHEST",
                NumberOfFrames = 1,
            };
            var slice = await SaveRasterSliceAsync(bytes, seriesIndex: 0, tempDir, cancellationToken).ConfigureAwait(false);
            seriesList =
            [
                new CtSeriesInfo
                {
                    SeriesIndex = 0,
                    Label = "CT Image",
                    Modality = "CT",
                    BodyPartExamined = "CHEST",
                    PreviewSliceIndex = 0,
                    Slices = [slice],
                },
            ];
        }

        if (seriesList.Count == 0)
            throw new InvalidOperationException("No DICOM series could be extracted from this upload.");

        var session = new CtScanViewerSession
        {
            SessionId = sessionId,
            UserId = userId,
            FileName = fileName,
            ContentType = contentType,
            SourceBytes = bytes,
            Metadata = metadata,
            Series = seriesList,
            TempDirectory = tempDir,
            PatientScanId = patientScanId,
            PatientId = patientId,
        };

        _store.Save(session);
        _logger.LogInformation(
            "CT viewer session {SessionId}: {SeriesCount} series, {SliceCount} total slice(s) for user {UserId}",
            sessionId, seriesList.Count, seriesList.Sum(s => s.Slices.Count), userId);

        return session;
    }

    private sealed class ExtractedSeries
    {
        public required CtSeriesInfo Info { get; init; }
        public PatientMedicalHistory? MetadataForStudy { get; init; }
    }

    private async Task<ExtractedSeries> ExtractSeriesFromDicomBytesAsync(
        byte[] bytes,
        int seriesIndex,
        string tempDir,
        CancellationToken cancellationToken)
    {
        var dicomFile = await OpenDicomAsync(bytes, cancellationToken).ConfigureAwait(false);
        var metadata = _metadataParser.Parse(dicomFile);
        var slices = await ExtractDicomSlicesAsync(dicomFile, metadata, seriesIndex, tempDir, cancellationToken)
            .ConfigureAwait(false);

        if (slices.Count == 0)
            throw new InvalidOperationException("No slices could be extracted from this DICOM file.");

        var previewIndex = PickPreviewSliceIndex(slices, metadata);
        var label = BuildSeriesLabel(metadata, seriesIndex);

        return new ExtractedSeries
        {
            MetadataForStudy = metadata,
            Info = new CtSeriesInfo
            {
                SeriesIndex = seriesIndex,
                SeriesInstanceUid = ReadSeriesUid(dicomFile.Dataset),
                Label = label,
                Modality = string.IsNullOrWhiteSpace(metadata.Modality) ? "CT" : metadata.Modality,
                SeriesDescription = metadata.SeriesDescription,
                BodyPartExamined = metadata.BodyPartExamined,
                PreviewSliceIndex = previewIndex,
                Slices = slices,
            },
        };
    }

    private async Task<(IReadOnlyList<CtSeriesInfo> Series, PatientMedicalHistory Metadata)> LoadSeriesFromZipAsync(
        byte[] bytes,
        string tempDir,
        CancellationToken cancellationToken)
    {
        var groupedBytes = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);

        using var zipMs = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(zipMs, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0 || string.IsNullOrWhiteSpace(entry.Name))
                continue;
            if (!AnalyticsDicomRouting.IsDicomUpload(entry.Name, null))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            await using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var fileBytes = ms.ToArray();
            if (fileBytes.Length == 0)
                continue;

            var dicomFile = await OpenDicomAsync(fileBytes, cancellationToken).ConfigureAwait(false);
            var seriesKey = ReadSeriesUid(dicomFile.Dataset);
            if (string.IsNullOrWhiteSpace(seriesKey))
                seriesKey = $"entry-{entry.FullName}";

            if (!groupedBytes.TryGetValue(seriesKey, out var list))
            {
                list = [];
                groupedBytes[seriesKey] = list;
            }

            list.Add(fileBytes);
        }

        if (groupedBytes.Count == 0)
            throw new InvalidOperationException("The ZIP archive contains no readable DICOM (.dcm) files.");

        var result = new List<CtSeriesInfo>();
        PatientMedicalHistory? studyMeta = null;
        var seriesIdx = 0;

        foreach (var kv in groupedBytes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var allSlices = new List<CtScanSliceInfo>();
            PatientMedicalHistory? seriesMeta = null;

            foreach (var fileBytes in kv.Value)
            {
                var dicomFile = await OpenDicomAsync(fileBytes, cancellationToken).ConfigureAwait(false);
                var meta = _metadataParser.Parse(dicomFile);
                seriesMeta ??= meta;
                studyMeta ??= meta;

                var slices = await ExtractDicomSlicesAsync(
                        dicomFile, meta, seriesIdx, tempDir, cancellationToken, allSlices.Count)
                    .ConfigureAwait(false);
                allSlices.AddRange(slices);
            }

            allSlices = allSlices.OrderBy(s => s.Index).ToList();
            seriesMeta ??= new PatientMedicalHistory();
            result.Add(new CtSeriesInfo
            {
                SeriesIndex = seriesIdx,
                SeriesInstanceUid = kv.Key.StartsWith("entry-", StringComparison.Ordinal) ? "" : kv.Key,
                Label = BuildSeriesLabel(seriesMeta, seriesIdx),
                Modality = seriesMeta.Modality,
                SeriesDescription = seriesMeta.SeriesDescription,
                BodyPartExamined = seriesMeta.BodyPartExamined,
                PreviewSliceIndex = PickPreviewSliceIndex(allSlices, seriesMeta),
                Slices = allSlices,
            });
            seriesIdx++;
        }

        return (result, studyMeta ?? new PatientMedicalHistory());
    }

    private async Task<List<CtScanSliceInfo>> ExtractDicomSlicesAsync(
        DicomFile dicomFile,
        PatientMedicalHistory metadata,
        int seriesIndex,
        string tempDir,
        CancellationToken cancellationToken,
        int sliceOffset = 0)
    {
        var slices = new List<CtScanSliceInfo>();
        var total = Math.Max(1, metadata.NumberOfFrames);
        var bodyPart = string.IsNullOrWhiteSpace(metadata.BodyPartExamined) ? "CHEST" : metadata.BodyPartExamined.ToUpperInvariant();
        var localIndex = 0;

        foreach (var frame in _sliceExtractor.EnumerateSlices(dicomFile, metadata, cancellationToken))
        {
            using (frame)
            {
                var globalIndex = sliceOffset + localIndex;
                var jpegName = $"s{seriesIndex:D2}_{globalIndex:D4}.jpg";
                var jpegPath = Path.Combine(tempDir, jpegName);
                await SaveRgbAsJpegAsync(frame.Pixels, jpegPath, cancellationToken).ConfigureAwait(false);

                slices.Add(new CtScanSliceInfo
                {
                    Index = globalIndex,
                    InstanceNumber = frame.InstanceNumber ?? (globalIndex + 1).ToString(CultureInfo.InvariantCulture),
                    Label = $"{metadata.Modality} {localIndex + 1}/{total} {bodyPart}".Trim(),
                    Width = frame.Pixels.Width,
                    Height = frame.Pixels.Height,
                    JpegFileName = jpegName,
                });
                localIndex++;
            }
        }

        return slices;
    }

    private async Task<CtScanSliceInfo> SaveRasterSliceAsync(
        byte[] bytes,
        int seriesIndex,
        string tempDir,
        CancellationToken cancellationToken)
    {
        const string jpegName = "s00_0000.jpg";
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

    private async Task<DicomFile> OpenDicomAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream(bytes, writable: false);
        return await _dicomLoader.LoadAsync(ms, cancellationToken).ConfigureAwait(false);
    }

    private static string ReadSeriesUid(DicomDataset ds)
    {
        try
        {
            return ds.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "") ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string BuildSeriesLabel(PatientMedicalHistory meta, int seriesIndex)
    {
        var parts = new[]
            {
                string.IsNullOrWhiteSpace(meta.Modality) ? null : meta.Modality.Trim(),
                string.IsNullOrWhiteSpace(meta.SeriesDescription) ? null : meta.SeriesDescription.Trim(),
                string.IsNullOrWhiteSpace(meta.BodyPartExamined) ? null : meta.BodyPartExamined.Trim(),
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Count > 0)
            return string.Join(" · ", parts);

        return $"Series {seriesIndex + 1}";
    }

    private static int PickPreviewSliceIndex(IReadOnlyList<CtScanSliceInfo> slices, PatientMedicalHistory meta)
    {
        if (slices.Count == 0)
            return 0;
        return slices.Count / 2;
    }

    private static string ResolveLayoutMode(IReadOnlyList<CtSeriesInfo> series)
    {
        var withSlices = series.Count(s => s.Slices.Count > 0);
        if (withSlices >= 2 && withSlices <= 4)
            return "grid";
        if (withSlices >= 2)
            return "grid";
        return "single";
    }

    private static bool IsZipUpload(string fileName, string contentType)
    {
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return true;
        return (contentType ?? "").Contains("zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendMeta(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        sb.AppendLine($"{label}: {value.Trim()}");
    }
}
