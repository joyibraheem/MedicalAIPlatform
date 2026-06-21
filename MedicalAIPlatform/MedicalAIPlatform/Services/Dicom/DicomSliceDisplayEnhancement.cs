using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MedicalAIPlatform.Services.Dicom;

/// <summary>
/// Makes derived DICOM presentations visible in the browser (SEG masks, label maps, bad VOI).
/// Does not change diagnostic intent — only display scaling before JPEG.encode / CNN gateways.
/// </summary>
public static class DicomSliceDisplayEnhancement
{
    /// <summary>Discrete label maps (e.g. SEG with values 0–N) need full-range scaling or they render black.</summary>
    private const byte DiscreteLabelMaxInclusive = 16;

    /// <summary>
    /// SEG / segmentation results often store tiny integers (0–1 or 0–5). Standard grayscale LUT maps them to black.
    /// Also fixes CT/X-Ray windowing that collapses dynamic range to a flat dark image.
    /// </summary>
    public static void EnhanceForWebDisplay(Image<Rgb24> img, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(img);

        byte maxSample = 0;
        ulong sumSample = 0;
        ulong pixelCount = 0;

        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref readonly var p = ref row[x];
                    var m = Math.Max(Math.Max(p.R, p.G), p.B);
                    if (m > maxSample) maxSample = m;
                    sumSample += m;
                    pixelCount++;
                }
            }
        });

        if (pixelCount == 0 || maxSample == 0)
            return;

        // Binary / multi-label segmentation stored as small integers (confirmed for QIN SEG: uint8 0–1).
        if (maxSample <= DiscreteLabelMaxInclusive)
        {
            var scale = 255f / maxSample;
            img.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        ref var p = ref row[x];
                        var m = Math.Max(Math.Max(p.R, p.G), p.B);
                        var v = (byte)Math.Clamp((int)Math.Round(m * scale), 0, 255);
                        p.R = v;
                        p.G = v;
                        p.B = v;
                    }
                }
            });

            logger.LogInformation(
                "DICOM display: expanded discrete label intensities for web view (maxStored={Max}).",
                maxSample);

            UpscaleSmallForegroundMaskToFillCanvas(img, logger);
            return;
        }

        // Collapsed VOI / wrong window: stretch luminance when the image is mostly flat and dark.
        byte minL = 255, maxL = 0;
        sumSample = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref readonly var p = ref row[x];
                    var lum = (byte)((177 * p.R + 621 * p.G + 306 * p.B) >> 10);
                    if (lum < minL) minL = lum;
                    if (lum > maxL) maxL = lum;
                    sumSample += lum;
                }
            }
        });

        var mean = sumSample / (double)pixelCount;
        var range = maxL - minL;
        var flatDark = maxL < 110 && (range < 42 || mean < 22);
        if (!flatDark || range <= 0)
            return;

        var denom = (float)range;
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var p = ref row[x];
                    var lum = (byte)((177 * p.R + 621 * p.G + 306 * p.B) >> 10);
                    var v = (byte)Math.Clamp((int)Math.Round((lum - minL) / denom * 255.0), 0, 255);
                    p.R = v;
                    p.G = v;
                    p.B = v;
                }
            }
        });

        logger.LogInformation(
            "DICOM display: auto contrast for flat/dark slice (L min={Min} max={Max} mean={Mean:F1}).",
            minL, maxL, mean);
    }

    /// <summary>Sum of RGB — higher means more ink on screen after label scaling (pick best SEG slice).</summary>
    public static double TotalRgbEnergy(Image<Rgb24> pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ulong sum = 0;
        pixels.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref readonly var p = ref row[x];
                    sum += (uint)p.R + p.G + p.B;
                }
            }
        });

        return sum;
    }

    /// <summary>
    /// SEG lesions often cover a tiny fraction of 512² — after CNN resize they disappear.
    /// Crop to foreground bbox (with margin) and stretch back to the original dimensions.
    /// </summary>
    private static void UpscaleSmallForegroundMaskToFillCanvas(Image<Rgb24> img, ILogger logger)
    {
        const byte fgThreshold = 28;
        var w = img.Width;
        var h = img.Height;
        if (w < 4 || h < 4)
            return;

        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;
        img.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref readonly var p = ref row[x];
                    if (p.R <= fgThreshold && p.G <= fgThreshold && p.B <= fgThreshold)
                        continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        });

        if (maxX < minX || maxY < minY)
            return;

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        var fracW = bw / (double)w;
        var fracH = bh / (double)h;
        var areaFrac = bw * bh / (double)(w * h);
        // Zoom whenever the mask is visually small on the slice (most lesion SEG uploads).
        if (areaFrac >= 0.24 && fracW >= 0.44 && fracH >= 0.44)
            return;

        var margin = Math.Max(8, (int)Math.Round(0.1 * Math.Max(bw, bh)));
        minX = Math.Max(0, minX - margin);
        minY = Math.Max(0, minY - margin);
        maxX = Math.Min(w - 1, maxX + margin);
        maxY = Math.Min(h - 1, maxY + margin);
        var cw = maxX - minX + 1;
        var ch = maxY - minY + 1;
        if (cw < 2 || ch < 2)
            return;

        var rect = new Rectangle(minX, minY, cw, ch);
        img.Mutate(ctx =>
        {
            ctx.Crop(rect);
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(w, h),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.NearestNeighbor,
            });
        });

        logger.LogInformation(
            "DICOM display: zoomed segmentation ROI ({Cw}×{Ch} px, {AreaFrac:P0} of slice) to fill preview ({W}×{H}).",
            cw,
            ch,
            areaFrac,
            w,
            h);
    }
}
