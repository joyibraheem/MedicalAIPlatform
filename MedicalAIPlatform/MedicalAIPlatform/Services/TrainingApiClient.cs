using System.Text.Json;
using System.Text.Json.Serialization;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Options;

namespace MedicalAIPlatform.Services;

public sealed class TrainingApiClient
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ModelRetrainingOptions _options;
    private readonly ILogger<TrainingApiClient> _logger;

    public TrainingApiClient(HttpClient http, Microsoft.Extensions.Options.IOptions<ModelRetrainingOptions> options,
        ILogger<TrainingApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TrainingApiResult?> StartTrainingAsync(string modelName, string datasetPath,
        CancellationToken ct = default)
    {
        var baseUrl = _options.TrainingApiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/training/start";

        var payload = new TrainingStartRequest
        {
            Model = MapModelForPython(modelName),
            DatasetPath = datasetPath
        };

        using var response = await _http.PostAsJsonAsync(url, payload, JsonOpts, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Training API failed ({Status}): {Body}", (int)response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<TrainingApiResult>(body, JsonOpts);
    }

    private static string MapModelForPython(string modelName) => modelName switch
    {
        ModelTrainingNames.LungCancer => "LungCancer",
        _ => modelName
    };
}

public sealed class TrainingStartRequest
{
    public string Model { get; set; } = "";
    public string DatasetPath { get; set; } = "";
}

public sealed class TrainingApiResult
{
    public string Model { get; set; } = "";

    public string Version { get; set; } = "";

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = "";

    public double? Accuracy { get; set; }

    [JsonPropertyName("f1_score")]
    public double? F1Score { get; set; }

    public double? Loss { get; set; }

    [JsonPropertyName("training_log_path")]
    public string? TrainingLogPath { get; set; }

    public int DatasetSize { get; set; }

    public string? Error { get; set; }
}
