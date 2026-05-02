using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MedicalAIPlatform.Services.Dicom;

/// <summary>Deterministic 224² RGB tensors with ImageNet normalization for CNN-style ONNX inputs.</summary>
public sealed class DicomSlicePreprocessor
{
    private readonly DicomPipelineOptions _opt;
    private readonly ILogger<DicomSlicePreprocessor> _log;

    public DicomSlicePreprocessor(
        Microsoft.Extensions.Options.IOptions<DicomPipelineOptions> opt,
        ILogger<DicomSlicePreprocessor> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public PreprocessedSliceTensor ToNchw224(SliceFrameRgb slice)
    {
        using var resized = Resize224(slice.Pixels);
        return PackTensor(slice.SliceIndex, slice.InstanceNumber, resized);
    }

    /// <summary>Single 224³ resize reused for ONNX tensors and outbound JPEG payloads to HTTP gateways.</summary>
    public (PreprocessedSliceTensor Tensor, byte[] JpegQuality224Bytes) TensorAndEncodedJpeg(SliceFrameRgb slice)
    {
        ArgumentNullException.ThrowIfNull(slice.Pixels);

        using var resized = Resize224(slice.Pixels);
        var tensor = PackTensor(slice.SliceIndex, slice.InstanceNumber, resized);
        byte[] jpeg;
        using (var ms = new MemoryStream(capacity: 64_000))
        {
            resized.SaveAsJpeg(ms, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = (byte)Math.Clamp(_opt.ExportJpegQuality, 60, 100) });
            jpeg = ms.ToArray();
        }

        return (tensor, jpeg);
    }

    private PreprocessedSliceTensor PackTensor(int sliceIx, string? instanceNumber, Image<Rgb24> rgb224)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dest = new float[3 * 224 * 224];
        ToNchwFloatNormalized(rgb224, dest);
        sw.Stop();
        if (sw.ElapsedMilliseconds > 75)
            _log.LogWarning("Slow preprocess slice {Ix}: {Ms} ms", sliceIx, sw.ElapsedMilliseconds);

        return new PreprocessedSliceTensor
        {
            SliceIndex = sliceIx,
            InstanceNumber = instanceNumber,
            Nchw224 = dest,
        };
    }

    private static Image<Rgb24> Resize224(Image<Rgb24> src)
    {
        ArgumentNullException.ThrowIfNull(src);
        var clone = src.Clone(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(224, 224),
                Mode = ResizeMode.Pad,
                PadColor = Color.Black,
            });
        });
        return clone;
    }

    /// <summary>Channel-first floats (ImageNet-style mean/std).</summary>
    private void ToNchwFloatNormalized(Image<Rgb24> img224, float[] nchw)
    {
        const int spatial = 224 * 224;
        img224.ProcessPixelRows(accessor =>
        {
            var ix = 0;
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++, ix++)
                {
                    var px = row[x];
                    float rf = px.R / 255f;
                    float gf = px.G / 255f;
                    float bf = px.B / 255f;

                    nchw[ix] = (float)((rf - _opt.ImageNetMeanR) / _opt.ImageNetStdR);
                    nchw[spatial + ix] = (float)((gf - _opt.ImageNetMeanG) / _opt.ImageNetStdG);
                    nchw[2 * spatial + ix] = (float)((bf - _opt.ImageNetMeanB) / _opt.ImageNetStdB);
                }
            }
        });
    }
}
