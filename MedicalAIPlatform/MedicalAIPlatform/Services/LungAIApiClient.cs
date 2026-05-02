using System.Net.Http.Headers;
using System.Text.Json;
using MedicalAIPlatform.Models;

namespace MedicalAIPlatform.Services;

public sealed class LungAIApiClient
{
    private readonly HttpClient _http;
    private readonly CheXNetApiEndpointOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public LungAIApiClient(HttpClient http, CheXNetApiEndpointOptions options)
    {
        _http = http;
        _options = options;
    }

    private Uri GetPredictCtUri()
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
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
        using var response = await _http.PostAsync(requestUri, form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = TryExtractDetail(body) ?? body;
            var hint = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? " Make sure the API is running on port 8000 or set LungAI:BaseUrl in appsettings."
                : "";
            throw new HttpRequestException($"LungAI CT API error ({(int)response.StatusCode}): {message}.{hint}");
        }

        var parsed = JsonSerializer.Deserialize<LungAICtResponse>(body, JsonOptions) ?? new LungAICtResponse();
        if (parsed.Probabilities is null)
            parsed.Probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (parsed.ClassNames is null)
            parsed.ClassNames = [];
        return parsed;
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
