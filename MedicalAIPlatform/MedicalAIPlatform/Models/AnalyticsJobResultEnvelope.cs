using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedicalAIPlatform.Models;

/// <summary>Persisted job outcome: lightweight summary for polling + full view state for result pages.</summary>
public sealed class AnalyticsJobResultEnvelope
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [JsonPropertyName("summary")]
    public AnalyticsCtJobResultDto Summary { get; init; } = new();

    [JsonPropertyName("viewState")]
    public AnalyticsSessionSnapshot? ViewState { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

    public static bool TryParse(string? json, out AnalyticsCtJobResultDto? summary, out AnalyticsSessionSnapshot? viewState)
    {
        summary = null;
        viewState = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            var envelope = JsonSerializer.Deserialize<AnalyticsJobResultEnvelope>(json, JsonOpts);
            if (envelope?.Summary is not null)
            {
                summary = envelope.Summary;
                viewState = envelope.ViewState;
                return true;
            }
        }
        catch
        {
            /* fall through to legacy shape */
        }

        try
        {
            summary = JsonSerializer.Deserialize<AnalyticsCtJobResultDto>(json, JsonOpts);
            return summary is not null;
        }
        catch
        {
            return false;
        }
    }
}
