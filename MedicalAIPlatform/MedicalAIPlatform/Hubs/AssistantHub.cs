using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MedicalAIPlatform.Hubs;

[Authorize(Policy = "VerifiedMedicalUser")]
public sealed class AssistantHub : Hub
{
    public static string GroupNameFor(string userId) => $"mai:user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var uid = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(uid))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNameFor(uid));

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var uid = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(uid))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameFor(uid));

        await base.OnDisconnectedAsync(exception);
    }
}
