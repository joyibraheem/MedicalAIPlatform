namespace MedicalAIPlatform.Options;

public sealed class DicomPipelineOptions
{
    public const string SectionName = "DicomPipeline";

    /// <summary>Path to ONNX model; when empty, HTTP backends are used instead.</summary>
    public string OnnxModelPath { get; set; } = "";

    public string OnnxInputTensorName { get; set; } = "input";
    public string OnnxOutputTensorName { get; set; } = "output";

    /// <summary>Labels aligned with ONNX output logits/probabilities (optional; indices used if omitted).</summary>
    public string[] OnnxClassLabels { get; set; } = [];

    public int InferenceBatchSize { get; set; } = 8;

    /// <summary>JPEG quality when encoding slices for HTTP APIs (1–100).</summary>
    public int ExportJpegQuality { get; set; } = 90;

    public double VotingThreshold { get; set; } = 0.5;

    public double ImageNetMeanR { get; set; } = 0.485f;
    public double ImageNetMeanG { get; set; } = 0.456f;
    public double ImageNetMeanB { get; set; } = 0.406f;
    public double ImageNetStdR { get; set; } = 0.229f;
    public double ImageNetStdG { get; set; } = 0.224f;
    public double ImageNetStdB { get; set; } = 0.225f;

    /// <summary>Fallback CT lung-style window if DICOM does not specify VOI LUT or window tags.</summary>
    public double DefaultCtWindowCenter { get; set; } = 40;
    public double DefaultCtWindowWidth { get; set; } = 400;
}
