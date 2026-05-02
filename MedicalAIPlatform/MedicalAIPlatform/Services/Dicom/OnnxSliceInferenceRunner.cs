using MedicalAIPlatform.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MedicalAIPlatform.Services.Dicom;

public sealed class OnnxSliceInferenceRunner : IDisposable
{
    private readonly ILogger<OnnxSliceInferenceRunner> _log;
    private readonly IHostEnvironment _env;
    private readonly object _gate = new();
    private InferenceSession? _session;
    private string? _loadedPath;

    public OnnxSliceInferenceRunner(ILogger<OnnxSliceInferenceRunner> log, IHostEnvironment env)
    {
        _log = log;
        _env = env;
    }

    public bool IsConfigured(DicomPipelineOptions cfg) => !string.IsNullOrWhiteSpace(cfg.OnnxModelPath);

    public IReadOnlyList<Dictionary<string, double>> InferBatch(
        IOptions<DicomPipelineOptions> optionsAccessor,
        IReadOnlyList<float[]> tensorsNchw,
        CancellationToken cancellationToken)
    {
        var cfg = optionsAccessor.Value;
        if (!IsConfigured(cfg))
            throw new InvalidOperationException("DicomPipeline:OnnxModelPath is empty.");

        var sess = GetOrCreate(cfg);
        cancellationToken.ThrowIfCancellationRequested();

        int n = tensorsNchw.Count;
        if (n == 0) return Array.Empty<Dictionary<string, double>>();

        const int plane = 224 * 224;
        const int perImage = 3 * plane;
        var batch = new float[n * perImage];
        for (int i = 0; i < n; i++)
        {
            var t = tensorsNchw[i];
            if (t.Length != perImage)
                throw new ArgumentException(nameof(tensorsNchw));
            Buffer.BlockCopy(t, 0, batch, i * perImage * sizeof(float), perImage * sizeof(float));
        }

        var input = NamedOnnxValue.CreateFromTensor(
            cfg.OnnxInputTensorName,
            new DenseTensor<float>(batch, new[] { n, 3, 224, 224 }));

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = sess.Run(new[] { input });
        var holder = results.SingleOrDefault(o => o.Name == cfg.OnnxOutputTensorName) ?? results.Single();
        var logits = holder.AsTensor<float>();
        if (logits is null)
            throw new InvalidOperationException($"Output {cfg.OnnxOutputTensorName} is missing or not float tensor.");

        int[] dimsArr = logits.Dimensions.ToArray();
        if (dimsArr.Length < 2)
            throw new InvalidOperationException("Expected output rank >= 2 (batch, labels).");

        int batchDim = dimsArr[0];
        int classDim = dimsArr[^1];

        if (batchDim != n)
            throw new InvalidOperationException($"ONNX batch mismatch: got {batchDim}, expected {n}.");

        float[] buf = logits.ToArray();

        var rows = new Dictionary<string, double>[n];
        string[] labels = cfg.OnnxClassLabels;
        for (int row = 0; row < n; row++)
        {
            var slice = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            Span<float> rowSlice = buf.AsSpan(row * classDim, classDim);

            if (!IsProbabilities(rowSlice))
            {
                SoftmaxInPlace(rowSlice);
            }

            for (int cls = 0; cls < classDim; cls++)
            {
                string name = labels is { Length: > 0 }
                    ? (cls < labels.Length ? labels[cls] : $"label_{cls}")
                    : $"class_{cls}";
                slice[name] = rowSlice[cls];
            }

            rows[row] = slice;
        }

        _log.LogInformation("ONNX batch {N}x{Classes} complete", n, classDim);
        return rows;
    }

    private static bool IsProbabilities(ReadOnlySpan<float> r)
    {
        double sum = 0;
        for (int i = 0; i < r.Length; i++) sum += r[i];
        return sum is > 0.95 and < 1.05;
    }

    private static void SoftmaxInPlace(Span<float> r)
    {
        float m = r.Length == 0 ? 0 : float.NegativeInfinity;
        for (int i = 0; i < r.Length; i++) m = Math.Max(m, r[i]);
        float s = 0f;
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = MathF.Exp(r[i] - m);
            s += r[i];
        }
        if (s <= 0f) return;
        for (int i = 0; i < r.Length; i++) r[i] /= s;
    }

    private InferenceSession GetOrCreate(DicomPipelineOptions cfg)
    {
        lock (_gate)
        {
            var path = ResolvePath(cfg.OnnxModelPath);
            if (_session != null && string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
                return _session;

            _session?.Dispose();
            if (!File.Exists(path))
                throw new FileNotFoundException("ONNX model not found.", path);

            _session = new InferenceSession(path, new Microsoft.ML.OnnxRuntime.SessionOptions());
            _loadedPath = path;
            _log.LogInformation("Loaded ONNX graph from {Path}", path);
            return _session;
        }
    }

    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(_env.ContentRootPath, path));

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
            _loadedPath = null;
        }
    }
}
