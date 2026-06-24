using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

/// <summary>Durable pointers to original imaging/text used for HITL retraining.</summary>
public sealed class TrainingSourceData
{
    public string ModelKey { get; set; } = "";

    public string? ImagePath { get; set; }

    public string? DicomPath { get; set; }

    public string? StudyInstanceUid { get; set; }

    public string? SeriesInstanceUid { get; set; }

    /// <summary>Extracted slice JPEG paths for volumetric CT/DICOM studies.</summary>
    public List<string> SliceImagePaths { get; set; } = [];

    public string? OriginalClinicalText { get; set; }

    public string? OriginalReport { get; set; }

    public string? CorrectedLabel { get; set; }

    public Guid? RelatedJobId { get; set; }

    public int? PatientScanId { get; set; }

    public int? PatientId { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static TrainingSourceData? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<TrainingSourceData>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
