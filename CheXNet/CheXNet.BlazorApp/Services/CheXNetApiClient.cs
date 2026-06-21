using System.Net.Http.Headers;
using System.Text.Json;
using CheXNet.BlazorApp.Models;

namespace CheXNet.BlazorApp.Services;

public sealed class CheXNetApiClient(HttpClient http, CheXNetApiEndpointOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private string Base() => options.BaseUrl.TrimEnd('/');

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

        using var response = await http.PostAsync(url, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // FastAPI error format: {"detail": "..."}
            var message = TryExtractFastApiDetail(body) ?? body;
            throw new HttpRequestException($"CheXNet API error ({(int)response.StatusCode}): {message}");
        }

        var parsed = JsonSerializer.Deserialize<CheXNetPredictionResponse>(body, JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("CheXNet API returned invalid JSON.");
        }

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
}

