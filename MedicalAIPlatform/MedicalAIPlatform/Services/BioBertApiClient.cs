using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public sealed class BioBertApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public BioBertApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<BioBertResponse> PredictAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new { text };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync("classify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"BioBERT API error ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<BioBertResponse>(body, JsonOptions) ?? new BioBertResponse();
        if (parsed.Entities is null)
            parsed.Entities = [];
        return parsed;
    }
}
