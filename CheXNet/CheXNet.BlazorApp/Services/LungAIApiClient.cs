using System.Net.Http.Headers;
using System.Text.Json;
using CheXNet.BlazorApp.Models;

namespace CheXNet.BlazorApp.Services;

/// <summary>
/// Client for LungAI CT scan inference (same backend as CheXNet, endpoint /predict/ct).
/// </summary>
public sealed class LungAIApiClient(HttpClient http, CheXNetApiEndpointOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Absolute URL for CT endpoint, e.g. http://localhost:8000/predict/ct</summary>
    private Uri GetPredictCtUri()
    {
        var baseUrl = options.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/predict/ct", UriKind.Absolute);
    }

    public async Task<LungAICtResponse> PredictCtAsync(
        byte[] imageBytes,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "ct.jpg" : fileName);

        var requestUri = GetPredictCtUri();
        using var response = await http.PostAsync(requestUri, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryExtractDetail(body) ?? body;
            var hint = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? " Restart the Blazor app so it can start the full API on port 8002, or set LungAI:BaseUrl in appsettings."
                : "";
            throw new HttpRequestException($"LungAI CT API error ({(int)response.StatusCode}): {message}.{hint}");
        }

        var parsed = JsonSerializer.Deserialize<LungAICtResponse>(body, JsonOptions);
        return parsed ?? new LungAICtResponse();
    }

    private static string? TryExtractDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                return d.GetString();
        }
        catch { }
        return null;
    }
}
