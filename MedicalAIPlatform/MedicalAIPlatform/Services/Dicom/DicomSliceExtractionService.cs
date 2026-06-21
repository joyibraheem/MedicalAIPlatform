using FellowOakDicom;
using FellowOakDicom.Imaging;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MedicalAIPlatform.Services.Dicom;

/// <summary>Renders pixel data frames to RGB bitmaps suitable for resizing and normalization.</summary>
public sealed class DicomSliceExtractionService
{
    private readonly ILogger<DicomSliceExtractionService> _log;

    public DicomSliceExtractionService(
        ILogger<DicomSliceExtractionService> log,
        Microsoft.Extensions.Options.IOptions<DicomPipelineOptions> opt)
    {
        _log = log;
        _ = opt.Value;
    }

    /// <summary>Enumerate frames; caller must dispose each <see cref="SliceFrameRgb"/>.</summary>
    public IEnumerable<SliceFrameRgb> EnumerateSlices(DicomFile file, PatientMedicalHistory windowHint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(windowHint);
        if (file.Dataset == null)
            throw new InvalidOperationException("DICOM file has no dataset.");

        var ds = file.Dataset;

        float? wc = windowHint.WindowCenter is { } cw ? (float)cw : null;
        float? ww = windowHint.WindowWidth is { } w ? (float)w : null;
        if (wc == null || ww == null || ww <= 0)
            TryReadWindowFromDataset(ds, ref wc, ref ww);

        var dicomImage = new DicomImage(ds);
        if (wc.HasValue && ww.HasValue && ww > 0)
        {
            dicomImage.WindowCenter = wc.Value;
            dicomImage.WindowWidth = ww.Value;
        }

        int nFrames = Math.Max(1, dicomImage.NumberOfFrames);
        _log.LogInformation("Rendering {Frames} slice(s); window center={WC} width={WW}", nFrames, wc, ww);

        var xferUid = file.FileMetaInfo?.TransferSyntax?.UID?.UID ?? "(unknown)";

        for (var i = 0; i < nFrames; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TryGetSingleInstanceNumber(ds, out string? instanceNumber);

            IImage rendered;
            try
            {
                rendered = dicomImage.RenderImage(i);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "DICOM RenderImage threw for frame {Frame}/{Frames}; TransferSyntaxUID={Xfer}",
                    i, nFrames, xferUid);
                throw new InvalidOperationException(
                    "Could not decode this DICOM frame for display. Compressed images need the fo-dicom native codecs " +
                    $"(JPEG / JPEG 2000 / etc.). Frame {i}/{nFrames}, transfer syntax UID={xferUid}. Inner: {ex.Message}",
                    ex);
            }

            if (rendered is null)
            {
                _log.LogError(
                    "DICOM RenderImage returned null for frame {Frame}/{Frames}; TransferSyntaxUID={Xfer}",
                    i, nFrames, xferUid);
                throw new InvalidOperationException(
                    "The DICOM library returned no rendered image for this frame — often JPEG/JPEG2000 decoding failed. " +
                    $"Frame {i}/{nFrames}, transfer syntax UID={xferUid}. On Windows install the VC++ x64 runtime " +
                    "(https://aka.ms/vs/17/release/vc_redist.x64.exe) and restart the app so fo-dicom.Codecs can load.");
            }

            using (rendered)
            {
                Image<Bgra32> sharp = TryExtractBgra32(rendered, dicomImage, ds, i, nFrames);

                using (sharp)
                {
                    Image<Rgb24>? rgb24;
                    try
                    {
                        rgb24 = sharp.CloneAs<Rgb24>();
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "CloneAs<Rgb24> failed for frame {Frame}/{Frames}", i, nFrames);
                        throw new InvalidOperationException(
                            $"Could not clone rendered DICOM frame {i} to RGB: {ex.Message}", ex);
                    }

                    if (rgb24 is null)
                    {
                        throw new InvalidOperationException(
                            $"DICOM frame {i}: CloneAs<Rgb24> returned null (unexpected). Transfer syntax UID={xferUid}");
                    }

                    DicomSliceDisplayEnhancement.EnhanceForWebDisplay(rgb24, _log);

                    yield return new SliceFrameRgb(i, instanceNumber, rgb24);
                }
            }
        }
    }

    /// <summary>
    /// Builds an ImageSharp <see cref="Image{Bgra32}"/> from fo-dicom <see cref="IImage"/>.
    /// Uses raw BGRA bytes first (<see cref="RawImageExtensions.AsBytes"/>) so we never hit unstable
    /// <c>AsSharpImage()</c> code paths that can throw <see cref="NullReferenceException"/> when the renderer
    /// returns a malformed wrapper image.
    /// </summary>
    private Image<Bgra32> TryExtractBgra32(
        IImage rendered,
        DicomImage dicomImage,
        DicomDataset ds,
        int frameIndex,
        int totalFrames)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(dicomImage);
        ArgumentNullException.ThrowIfNull(ds);

        int w = Math.Max(1, dicomImage.Width);
        int h = Math.Max(1, dicomImage.Height);
        TryReadColumnsRows(ds, ref w, ref h);

        byte[] bytes;
        try
        {
            bytes = RawImageExtensions.AsBytes(rendered);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "RawImageExtensions.AsBytes failed for frame {Frame}/{Frames}",
                frameIndex, totalFrames);
            bytes = Array.Empty<byte>();
        }

        if (bytes.Length > 0)
        {
            var expectedLen = checked(w * h * 4);
            if (bytes.Length >= expectedLen)
            {
                _log.LogDebug(
                    "Frame {Frame}/{Frames}: ImageSharp via raw BGRA ({W}x{H}, {Len} bytes).",
                    frameIndex, totalFrames, w, h, bytes.Length);

                ReadOnlySpan<byte> span = bytes.AsSpan(0, expectedLen);
                return Image.LoadPixelData<Bgra32>(span, w, h);
            }

            var pxBytes = bytes.Length / 4;
            if (pxBytes * 4 == bytes.Length
                && TryInferDimensionsFromPixels(pxBytes, w, h, out int iw, out int ih)
                && iw * ih * 4 <= bytes.Length)
            {
                int len = checked(iw * ih * 4);
                _log.LogInformation(
                    "Frame {Frame}/{Frames}: using inferred size {Iw}x{Ih} (buffer={Len} px={Px}) tags/DicomImage were {W}x{H}).",
                    frameIndex, totalFrames, iw, ih, bytes.Length, pxBytes, w, h);

                return Image.LoadPixelData<Bgra32>(bytes.AsSpan(0, len), iw, ih);
            }

            _log.LogWarning(
                "Raw BGRA buffer length {Len} mismatches expected {Expected} ({W}x{H}) frame {Frame}/{Frames}",
                bytes.Length, expectedLen, w, h, frameIndex, totalFrames);
        }

        // Last resort: extension bridge (known to NullReference internally on some IImage wrappers).
        try
        {
            var fromBridge = rendered.AsSharpImage();
            if (fromBridge is not null)
                return fromBridge;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "AsSharpImage fallback failed frame {Frame}/{Frames}",
                frameIndex, totalFrames);
        }

        throw new InvalidOperationException(
            "Could not convert this DICOM frame to a bitmap after decoding. " +
            $"Frame {frameIndex}/{totalFrames}; RowsxColumns={TryFormatColumnsRows(ds)}; " +
            $"DicomImage {dicomImage.Width}×{dicomImage.Height}; raw buffer {(bytes.Length == 0 ? "missing" : bytes.Length + " bytes")}. ");
    }

    private static void TryReadColumnsRows(DicomDataset ds, ref int w, ref int h)
    {
        try
        {
            if (ds.TryGetSingleValue(DicomTag.Columns, out ushort c) && c > 0)
                w = c;
            if (ds.TryGetSingleValue(DicomTag.Rows, out ushort r) && r > 0)
                h = r;
        }
        catch { /* malformed optional tags */ }
    }

    private static bool TryInferDimensionsFromPixels(int px, int hintW, int hintH, out int iw, out int ih)
    {
        iw = hintW;
        ih = hintH;
        if (px <= 0) return false;

        if (hintW > 0 && hintH > 0 && px == hintW * hintH)
        {
            iw = hintW;
            ih = hintH;
            return true;
        }

        int s = (int)Math.Round(Math.Sqrt(px));
        if (s > 0 && s * s == px)
        {
            iw = s;
            ih = s;
            return true;
        }

        if (hintW > 0 && px % hintW == 0)
        {
            int ratioH = px / hintW;
            if (ratioH > 0)
            {
                iw = hintW;
                ih = ratioH;
                return true;
            }
        }

        return false;
    }

    private static string TryFormatColumnsRows(DicomDataset ds)
    {
        try
        {
            ushort c = 0, r = 0;
            ds.TryGetSingleValue(DicomTag.Columns, out c);
            ds.TryGetSingleValue(DicomTag.Rows, out r);
            return $"{r}x{c}";
        }
        catch
        {
            return "?x?";
        }
    }

    private static void TryReadWindowFromDataset(DicomDataset ds, ref float? wc, ref float? ww)
    {
        if (wc != null && ww != null && ww > 0) return;
        double? cw = ParseFirstDsValue(ds, DicomTag.WindowCenter);
        double? width = ParseFirstDsValue(ds, DicomTag.WindowWidth);
        if (cw is { } cn && width is { } wwv && wwv > 0)
        {
            wc ??= (float)cn;
            ww ??= (float)wwv;
        }
    }

    static double? ParseFirstDsValue(DicomDataset ds, DicomTag tag)
    {
        try
        {
            if (!ds.Contains(tag)) return null;
            var raw = ds.GetSingleValue<string>(tag)?.Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            var part = raw.Split('\\')[0];
            return double.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d : null;
        }
        catch
        {
            return null;
        }
    }

    private static void TryGetSingleInstanceNumber(DicomDataset ds, out string? instanceNumber)
    {
        instanceNumber = null;
        try
        {
            if (ds.TryGetSingleValue(DicomTag.InstanceNumber, out int v))
                instanceNumber = v.ToString();
        }
        catch { /* malformed optional tag */ }
    }
}

/// <summary>Rendered slice RGB buffer (224-preprocess input).</summary>
public sealed class SliceFrameRgb : IDisposable
{
    public SliceFrameRgb(int sliceIndex, string? instanceNumber, Image<Rgb24> pixels)
    {
        SliceIndex = sliceIndex;
        InstanceNumber = instanceNumber;
        Pixels = pixels;
    }

    public int SliceIndex { get; }
    public string? InstanceNumber { get; }
    public Image<Rgb24> Pixels { get; }

    public void Dispose() => Pixels?.Dispose();
}
