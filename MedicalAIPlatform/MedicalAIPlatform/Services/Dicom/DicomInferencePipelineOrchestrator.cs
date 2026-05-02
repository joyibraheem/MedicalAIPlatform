using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;
using Microsoft.Extensions.Options;

namespace MedicalAIPlatform.Services.Dicom;

/// <summary>Coordinates loader → slices → preprocessing → inference (batched ONNX or HTTP) → aggregation.</summary>
public sealed class DicomInferencePipelineOrchestrator
{
    private readonly IDicomDatasetLoaderService _loader;
    private readonly DicomSliceExtractionService _slices;
    private readonly DicomMetadataParser _meta;
    private readonly DicomSlicePreprocessor _prep;
    private readonly PredictionAggregationEngine _fuse;
    private readonly OnnxSliceInferenceRunner _onnx;
    private readonly CheXNetApiClient _chexNet;
    private readonly LungAIApiClient _lungAi;
    private readonly IOptions<DicomPipelineOptions> _pipeline;
    private readonly ILogger<DicomInferencePipelineOrchestrator> _log;

    public DicomInferencePipelineOrchestrator(
        IDicomDatasetLoaderService loader,
        DicomSliceExtractionService slices,
        DicomMetadataParser meta,
        DicomSlicePreprocessor prep,
        PredictionAggregationEngine fuse,
        OnnxSliceInferenceRunner onnx,
        CheXNetApiClient chexNet,
        LungAIApiClient lungAi,
        IOptions<DicomPipelineOptions> pipeline,
        ILogger<DicomInferencePipelineOrchestrator> log)
    {
        _loader = loader;
        _slices = slices;
        _meta = meta;
        _prep = prep;
        _fuse = fuse;
        _onnx = onnx;
        _chexNet = chexNet;
        _lungAi = lungAi;
        _pipeline = pipeline;
        _log = log;
    }

    public async Task<DicomInferencePipelineResponse> RunAsync(
        Stream dicomStream,
        DicomAggregationMethod aggregation,
        DicomInferenceBackend backend,
        CancellationToken cancellationToken)
    {
        var stats = new DicomPipelineStats { InferenceBackend = backend.ToString() };
        var notes = new List<string>();

        _log.LogInformation("DICOM pipeline: load");

        var file = await _loader.LoadAsync(dicomStream, cancellationToken).ConfigureAwait(false);
        var clinical = _meta.Parse(file);
        _log.LogInformation("DICOM metadata: modality={Modality}, frames={Frames}", clinical.Modality, clinical.NumberOfFrames);

        if (backend == DicomInferenceBackend.OnnxClassifier && !_onnx.IsConfigured(_pipeline.Value))
        {
            throw new InvalidOperationException(
                "DicomInferenceBackend.OnnxClassifier requires DicomPipeline:OnnxModelPath to reference a reachable ONNX model file.");
        }

        var slicePredictions = new List<DicomSlicePredictionDto>();
        var scoreRows = new List<Dictionary<string, double>>();
        byte[]? repVizJpeg = null;
        var repVizCrit = -1d;

        long prepMs = 0;
        long inferMs = 0;
        int onnxBatches = 0;

        // ONNX uses fixed micro-batches; HTTP issues one request per slice (bounded by study size in practice).
        var batchTensors = new List<PreprocessedSliceTensor>();
        int batchSize = Math.Max(1, _pipeline.Value.InferenceBatchSize);

        try
        {
            foreach (var frame in _slices.EnumerateSlices(file, clinical, cancellationToken))
            {
                using (frame)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var swPrep = System.Diagnostics.Stopwatch.StartNew();
                    var (tensor, jpeg) = _prep.TensorAndEncodedJpeg(frame);
                    swPrep.Stop();
                    prepMs += swPrep.ElapsedMilliseconds;

                    if (backend == DicomInferenceBackend.OnnxClassifier)
                    {
                        batchTensors.Add(tensor);
                        if (batchTensors.Count >= batchSize)
                        {
                            var swOnnx = System.Diagnostics.Stopwatch.StartNew();
                            var batchDicts = _onnx.InferBatch(_pipeline, batchTensors.Select(t => t.Nchw224).ToList(), cancellationToken);
                            swOnnx.Stop();
                            onnxBatches++;
                            inferMs += swOnnx.ElapsedMilliseconds;
                            long perSliceMs = batchDicts.Count == 0 ? 0 : Math.Max(1, swOnnx.ElapsedMilliseconds / batchDicts.Count);
                            HydrateOnnxRows(batchDicts, batchTensors, perSliceMs, slicePredictions, scoreRows);
                            batchTensors.Clear();
                        }

                        continue;
                    }

                    // HTTP backends
                    var swHttp = System.Diagnostics.Stopwatch.StartNew();
                    Dictionary<string, double> probs = backend switch
                    {
                        DicomInferenceBackend.HttpCheXNetChest => await InferCheXNetSlice(jpeg, frame.SliceIndex, cancellationToken)
                            .ConfigureAwait(false),
                        DicomInferenceBackend.HttpLungCtClassifier => await InferLungCtSlice(jpeg, frame.SliceIndex, cancellationToken)
                            .ConfigureAwait(false),
                        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null),
                    };
                    swHttp.Stop();
                    inferMs += swHttp.ElapsedMilliseconds;

                    AppendHttpSlice(slicePredictions, scoreRows, frame.SliceIndex, frame.InstanceNumber, probs, swHttp.ElapsedMilliseconds);

                    if (jpeg.Length > 0)
                    {
                        double maxProb = probs.Count == 0 ? 0 : probs.Values.Max();
                        if (maxProb > repVizCrit || repVizJpeg is null)
                        {
                            repVizCrit = maxProb;
                            repVizJpeg = (byte[])jpeg.Clone();
                        }
                    }
                }
            }

            // Flush trailing ONNX batch
            if (backend == DicomInferenceBackend.OnnxClassifier && batchTensors.Count > 0)
            {
                var swOnnx = System.Diagnostics.Stopwatch.StartNew();
                var batchDicts = _onnx.InferBatch(_pipeline, batchTensors.Select(t => t.Nchw224).ToList(), cancellationToken);
                swOnnx.Stop();
                onnxBatches++;
                inferMs += swOnnx.ElapsedMilliseconds;
                long perSliceMs = batchDicts.Count == 0 ? 0 : Math.Max(1, swOnnx.ElapsedMilliseconds / batchDicts.Count);
                HydrateOnnxRows(batchDicts, batchTensors, perSliceMs, slicePredictions, scoreRows);
                batchTensors.Clear();
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DICOM inference failed");
            throw;
        }

        if (slicePredictions.Count == 0)
            notes.Add("warning: zero slices produced scores (empty series or preprocess failure)");

        slicePredictions.Sort((a, b) => a.SliceIndex.CompareTo(b.SliceIndex));

        _log.LogInformation("DICOM pipeline: aggregation {Method}", aggregation);
        AggregationResult fused = _fuse.Fuse(scoreRows, aggregation, cancellationToken);

        string? vizPrevMime = null;
        string? vizPrevB64 = null;
        string? vizHmMime = null;
        string? vizHmB64 = null;
        string? vizHmClass = null;

        if (backend == DicomInferenceBackend.HttpCheXNetChest && repVizJpeg is { Length: > 0 })
        {
            vizPrevMime = "image/jpeg";
            vizPrevB64 = Convert.ToBase64String(repVizJpeg);
            var topKv = fused.FinalPrediction.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(topKv.Key))
            {
                vizHmClass = topKv.Key;
                try
                {
                    var map = await _chexNet
                        .PredictAsync(
                            repVizJpeg,
                            "dicom_representative_slice.jpg",
                            "image/jpeg",
                            models: "CheXNet",
                            heatmapClass: topKv.Key,
                            topK: 14,
                            cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (map.TryGetValue("CheXNet", out var heatPr))
                    {
                        var h = heatPr.Heatmap;
                        if (!string.IsNullOrWhiteSpace(h?.ImageBase64))
                        {
                            vizHmMime = string.IsNullOrWhiteSpace(h!.Mime) ? "image/png" : h.Mime;
                            vizHmB64 = h.ImageBase64;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "CheXNet heatmap request failed for representative slice, class={Class}", topKv.Key);
                }
            }
        }
        else if (backend == DicomInferenceBackend.HttpLungCtClassifier && repVizJpeg is { Length: > 0 })
        {
            vizPrevMime = "image/jpeg";
            vizPrevB64 = Convert.ToBase64String(repVizJpeg);
        }

        var resp = new DicomInferencePipelineResponse
        {
            FinalPrediction = fused.FinalPrediction.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            SlicePredictions = slicePredictions,
            ConfidenceScores = fused.FinalPrediction.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
            AggregationMethod = fused.MethodName,
            Metadata =
            {
                PatientMedicalHistory = clinical,
                StudyNotes = notes,
            },
            PipelineStats = stats,
            VisualizationPreviewMime = vizPrevMime,
            VisualizationPreviewBase64 = vizPrevB64,
            VisualizationHeatmapMime = vizHmMime,
            VisualizationHeatmapBase64 = vizHmB64,
            VisualizationHeatmapClass = vizHmClass,
        };

        stats.TotalSlices = slicePredictions.Count;
        stats.BatchCount = onnxBatches;
        stats.TotalPreprocessMs = prepMs;
        stats.TotalInferenceMs = inferMs;
        stats.InferenceBackend = backend.ToString();
        return resp;
    }

    private static void HydrateOnnxRows(
        IReadOnlyList<Dictionary<string, double>> batchDicts,
        IReadOnlyList<PreprocessedSliceTensor> batchTensors,
        long perSliceMs,
        List<DicomSlicePredictionDto> slicePredictions,
        List<Dictionary<string, double>> scoreRows)
    {
        for (int i = 0; i < batchDicts.Count; i++)
        {
            var probs = batchDicts[i];
            var meta = batchTensors[i];
            Top1(probs, out string? top, out double topVal);
            slicePredictions.Add(new DicomSlicePredictionDto
            {
                SliceIndex = meta.SliceIndex,
                InstanceNumber = meta.InstanceNumber,
                LabelScores = new Dictionary<string, double>(probs, StringComparer.OrdinalIgnoreCase),
                TopLabel = top,
                TopScore = topVal,
                InferenceMs = perSliceMs,
            });
            scoreRows.Add(new Dictionary<string, double>(probs, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void AppendHttpSlice(
        List<DicomSlicePredictionDto> slicePredictions,
        List<Dictionary<string, double>> scoreRows,
        int sliceIndex,
        string? instanceNumber,
        Dictionary<string, double> probs,
        long inferMs)
    {
        Top1(probs, out string? top, out double topVal);
        slicePredictions.Add(new DicomSlicePredictionDto
        {
            SliceIndex = sliceIndex,
            InstanceNumber = instanceNumber,
            LabelScores = new Dictionary<string, double>(probs, StringComparer.OrdinalIgnoreCase),
            TopLabel = top,
            TopScore = topVal,
            InferenceMs = inferMs,
        });
        scoreRows.Add(new Dictionary<string, double>(probs, StringComparer.OrdinalIgnoreCase));
    }

    private static void Top1(Dictionary<string, double> probs, out string? top, out double topVal)
    {
        top = null;
        topVal = 0;
        foreach (var kv in probs)
        {
            if (top is null || kv.Value > topVal)
            {
                top = kv.Key;
                topVal = kv.Value;
            }
        }
    }

    private async Task<Dictionary<string, double>> InferCheXNetSlice(byte[] jpeg, int sliceIndex, CancellationToken ct)
    {
        var map = await _chexNet.PredictAsync(
            jpeg,
            $"slice_{sliceIndex}.jpg",
            "image/jpeg",
            models: "CheXNet",
            heatmapClass: null,
            topK: 14,
            cancellationToken: ct).ConfigureAwait(false);

        if (!map.TryGetValue("CheXNet", out var pr))
            throw new InvalidOperationException("CheXNet response missing model key.");
        return new Dictionary<string, double>(pr.Probabilities, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, double>> InferLungCtSlice(byte[] jpeg, int sliceIndex, CancellationToken ct)
    {
        var pr = await _lungAi.PredictCtAsync(jpeg, $"slice_{sliceIndex}.jpg", "image/jpeg", ct).ConfigureAwait(false);
        if (pr.Probabilities.Count == 0 && !string.IsNullOrEmpty(pr.Error))
            throw new InvalidOperationException($"LungAI CT: {pr.Error}");
        return new Dictionary<string, double>(pr.Probabilities, StringComparer.OrdinalIgnoreCase);
    }
}
