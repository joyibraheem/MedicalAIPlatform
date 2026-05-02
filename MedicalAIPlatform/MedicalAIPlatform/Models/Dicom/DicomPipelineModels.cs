using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models.Dicom;

/// <summary>How slice-level scores are fused into a study-level result.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DicomAggregationMethod
{
    MaxPooling,
    AveragePooling,
    ThresholdVoting
}

/// <summary>Which 2D model backend evaluates each resized slice.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DicomInferenceBackend
{
    /// <summary>In-process ONNX (optional <c>DicomPipeline:OnnxModelPath</c>).</summary>
    OnnxClassifier,
    /// <summary>HTTP CheXNet multi-label thorax endpoint (requires CheXNet API).</summary>
    HttpCheXNetChest,
    /// <summary>Lung CT HTTP classifier (/predict/ct).</summary>
    HttpLungCtClassifier
}

/// <summary>Clinical context parsed from DICOM headers (PHI-aware: callers should persist according to policy).</summary>
public sealed class PatientMedicalHistory
{
    public string PatientId { get; set; } = "";
    public string PatientName { get; set; } = "";
    public int? PatientAgeYears { get; set; }
    public string PatientSex { get; set; } = "";
    public string StudyInstanceUid { get; set; } = "";
    public string StudyId { get; set; } = "";
    public DateTimeOffset? StudyDateTime { get; set; }
    public string Modality { get; set; } = "";
    public string BodyPartExamined { get; set; } = "";
    public string SeriesDescription { get; set; } = "";
    public string StudyDescription { get; set; } = "";
    /// <summary>Presentation window center/width applied for CT visualization when tags are present.</summary>
    public double? WindowCenter { get; set; }
    public double? WindowWidth { get; set; }
    public int NumberOfFrames { get; set; }
    public Dictionary<string, string> AdditionalTags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class DicomSlicePredictionDto
{
    public int SliceIndex { get; set; }
    public string? InstanceNumber { get; set; }
    /// <summary>Per-label probabilities or scores for this slice (model-dependent).</summary>
    public Dictionary<string, double> LabelScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TopLabel { get; set; }
    public double? TopScore { get; set; }
    public long InferenceMs { get; set; }
}

/// <summary>Context attached to inference results (clinical header + lightweight pipeline annotations).</summary>
public sealed class DicomInferenceMetadata
{
    /// <summary>Structured subset of DICOM patient/study demographics and acquisition context.</summary>
    [JsonPropertyName("patient_medical_history")]
    public PatientMedicalHistory PatientMedicalHistory { get; set; } = new();

    [JsonPropertyName("study_notes")]
    public List<string> StudyNotes { get; set; } = [];
}

public sealed class DicomInferencePipelineResponse
{
    [JsonPropertyName("final_prediction")]
    public Dictionary<string, double> FinalPrediction { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("slice_predictions")]
    public List<DicomSlicePredictionDto> SlicePredictions { get; set; } = [];

    [JsonPropertyName("confidence_scores")]
    public Dictionary<string, double> ConfidenceScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("aggregation_method")]
    public string AggregationMethod { get; set; } = "";

    [JsonPropertyName("metadata")]
    public DicomInferenceMetadata Metadata { get; set; } = new();

    /// <summary>Optional ONNX / HTTP instrumentation for audit trails.</summary>
    [JsonPropertyName("pipeline_stats")]
    public DicomPipelineStats? PipelineStats { get; set; }

    /// <summary>Representative 2D slice encoded for UI (JPEG from preprocessor).</summary>
    [JsonPropertyName("visualization_preview_mime")]
    public string? VisualizationPreviewMime { get; set; }

    [JsonPropertyName("visualization_preview_base64")]
    public string? VisualizationPreviewBase64 { get; set; }

    /// <summary>Optional CheXNet attention map for fused top class.</summary>
    [JsonPropertyName("visualization_heatmap_mime")]
    public string? VisualizationHeatmapMime { get; set; }

    [JsonPropertyName("visualization_heatmap_base64")]
    public string? VisualizationHeatmapBase64 { get; set; }

    [JsonPropertyName("visualization_heatmap_class")]
    public string? VisualizationHeatmapClass { get; set; }
}

public sealed class DicomPipelineStats
{
    public int TotalSlices { get; set; }
    public int BatchCount { get; set; }
    public long TotalPreprocessMs { get; set; }
    public long TotalInferenceMs { get; set; }
    public string InferenceBackend { get; set; } = "";
}

/// <summary>Internal: one slice tensor ready for model input.</summary>
public sealed class PreprocessedSliceTensor
{
    public int SliceIndex { get; set; }
    public string? InstanceNumber { get; set; }
    /// <summary>Row-major NCHW chunk for this single image: length 3*224*224.</summary>
    public float[] Nchw224 { get; set; } = [];
}
