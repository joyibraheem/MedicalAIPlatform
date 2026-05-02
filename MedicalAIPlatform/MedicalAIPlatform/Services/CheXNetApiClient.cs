using System.Net.Http.Headers;
using System.Text.Json;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public sealed class CheXNetApiClient
{
    private readonly HttpClient _http;
    private readonly CheXNetApiEndpointOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CheXNetApiClient(HttpClient http, CheXNetApiEndpointOptions options)
    {
        _http = http;
        _options = options;
    }

    private string Base() => _options.BaseUrl.TrimEnd('/');

    public async Task<Dictionary<string, CheXNetPredictionResponse>> PredictAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        string models = "CheXNet",
        string? heatmapClass = null,
        int topK = 14,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();

        using var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);

        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "xray.jpg" : fileName);
        form.Add(new StringContent(models), "models");

        var path = $"predict?top_k={Math.Clamp(topK, 1, 14)}";
        if (!string.IsNullOrWhiteSpace(heatmapClass))
        {
            path += $"&heatmap_class={Uri.EscapeDataString(heatmapClass)}";
        }
        var url = $"{Base()}/{path}";

        using var response = await _http.PostAsync(url, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryExtractFastApiDetail(body) ?? body;
            throw new HttpRequestException($"CheXNet API error ({(int)response.StatusCode}): {message}");
        }

        var parsed = JsonSerializer.Deserialize<CheXNetPredictionResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("CheXNet API returned invalid JSON.");
        }

        NormalizeCheXNetResponse(parsed);

        return new Dictionary<string, CheXNetPredictionResponse>(StringComparer.OrdinalIgnoreCase)
        {
            ["CheXNet"] = parsed
        };
    }

    private static string? TryExtractFastApiDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
            {
                return detail.GetString();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    /// <summary>Deserializer can leave nested objects null; views assume non-null dictionaries / heatmap.</summary>
    private static void NormalizeCheXNetResponse(CheXNetPredictionResponse p)
    {
        p.ClassNames ??= [];
        p.Probabilities ??= new Dictionary<string, double>(StringComparer.Ordinal);
        p.TopK ??= [];
        p.Heatmap ??= new CheXNetHeatmap();
        p.Device ??= "";
    }
}
