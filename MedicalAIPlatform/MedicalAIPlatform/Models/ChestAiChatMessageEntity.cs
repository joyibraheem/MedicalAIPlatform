namespace MedicalAIPlatform.Models;

/// <summary>Persisted MedAI / ChestAI assistant conversation row.</summary>
public sealed class ChestAiChatMessageEntity
{
    public long Id { get; set; }

    public string UserId { get; set; } = "";

    /// <summary>user | assistant | system</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    /// <summary>Optional JSON metadata (e.g. linked job id, CT links).</summary>
    public string? MetadataJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Optional correlation to background job.</summary>
    public Guid? RelatedJobId { get; set; }
}
