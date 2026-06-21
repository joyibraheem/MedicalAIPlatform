using System.Text.Json.Serialization;

namespace CheXNet.BlazorApp.Models;

public sealed class BioBertEntity
{
    [JsonPropertyName("entity_group")]
    public string EntityGroup { get; set; } = "";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("word")]
    public string Word { get; set; } = "";

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("end")]
    public int End { get; set; }
}

public sealed class BioBertResponse
{
    [JsonPropertyName("entities")]
    public List<BioBertEntity> Entities { get; set; } = [];
}
