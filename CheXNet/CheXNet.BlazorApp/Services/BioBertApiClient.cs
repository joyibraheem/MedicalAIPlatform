using System.Text;
using System.Text.Json;
using CheXNet.BlazorApp.Models;

namespace CheXNet.BlazorApp.Services;

public sealed class BioBertApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<BioBertResponse> PredictAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new { text };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await http.PostAsync("classify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"BioBERT API error ({(int)response.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<BioBertResponse>(body, JsonOptions);
        return parsed ?? new BioBertResponse();
    }
}
