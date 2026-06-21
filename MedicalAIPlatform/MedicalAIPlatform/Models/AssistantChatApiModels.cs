namespace MedicalAIPlatform.Models;

public sealed class AssistantChatEnqueueRequest
{
    public string Message { get; set; } = "";

    public List<ChatMessage>? History { get; set; }

    /// <summary>Optional patient chart context (e.g. from Patient/Details) so the assistant can scope reports.</summary>
    public int? PatientContextId { get; set; }
}

public sealed class ChestAiChatMessageDto
{
    public string Role { get; set; } = "";

    public string Content { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? RelatedJobId { get; set; }

    public string? MetadataJson { get; set; }
}
