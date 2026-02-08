namespace MedicalAIPlatform.Models;

public sealed class ChatMessage
{
    public string Role { get; set; } = "user"; // "system" | "user" | "assistant"
    public string Content { get; set; } = string.Empty;
}
