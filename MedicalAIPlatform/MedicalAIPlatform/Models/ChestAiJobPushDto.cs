namespace MedicalAIPlatform.Models;

/// <summary>Realtime payload pushed to the signed-in user's SignalR group.</summary>
public sealed class ChestAiJobPushDto
{
    public string Kind { get; set; } = "";

    public Guid JobId { get; set; }

    public string Status { get; set; } = "";

    public AnalyticsCtJobResultDto? Result { get; set; }

    public string? Error { get; set; }

    public string? AssistantContent { get; set; }
}
