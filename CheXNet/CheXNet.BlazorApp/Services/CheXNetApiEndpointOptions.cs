namespace CheXNet.BlazorApp.Services;

/// <summary>
/// Mutable base URL for the CheXNet/LungAI API. Updated by the hosted service when it starts the API on an alternate port (e.g. 8002).
/// </summary>
public sealed class CheXNetApiEndpointOptions
{
    /// <summary>Base URL for the API (e.g. http://localhost:8000/ or http://localhost:8002/).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000/";
}
