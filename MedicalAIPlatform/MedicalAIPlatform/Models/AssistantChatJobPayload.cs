namespace MedicalAIPlatform.Models;

/// <summary>Stored in <see cref="ChestAiBackgroundJob.InputPayloadJson"/> for assistant jobs.</summary>
public sealed class AssistantChatJobPayload
{
    public string Message { get; set; } = "";

    public List<ChatMessage> History { get; set; } = [];

    public int? PatientContextId { get; set; }
}
