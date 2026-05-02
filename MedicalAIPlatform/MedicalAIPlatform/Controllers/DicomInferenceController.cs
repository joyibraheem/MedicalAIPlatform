using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Services.Dicom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MedicalAIPlatform.Controllers;

/// <summary>Hospital-style DICOM upload → slice inference → aggregation JSON.</summary>
[ApiController]
[Route("api/dicom")]
[Authorize(Roles = "Doctor,Admin")]
public sealed class DicomInferenceController : ControllerBase
{
    private readonly DicomInferencePipelineOrchestrator _pipeline;
    private readonly ILogger<DicomInferenceController> _log;

    public DicomInferenceController(
        DicomInferencePipelineOrchestrator pipeline,
        ILogger<DicomInferenceController> log)
    {
        _pipeline = pipeline;
        _log = log;
    }

    /// <summary>Upload a DICOM (single or multi-frame). Returns aggregated study-level scores.</summary>
    [HttpPost("slice-inference")]
    [RequestFormLimits(MultipartBodyLengthLimit = 536_870_912)]
    public async Task<ActionResult<DicomInferencePipelineResponse>> SliceInference(
        IFormFile file,
        [FromForm] DicomAggregationMethod aggregation = DicomAggregationMethod.MaxPooling,
        [FromForm] DicomInferenceBackend backend = DicomInferenceBackend.HttpCheXNetChest,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest("A non-empty DICOM file is required.");

        await using var stream = file.OpenReadStream();
        try
        {
            var result = await _pipeline.RunAsync(stream, aggregation, backend, cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
        catch (FileNotFoundException ex)
        {
            _log.LogWarning(ex, "DICOM pipeline file/model issue");
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex)
        {
            _log.LogWarning(ex, "DICOM pipeline configuration");
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
