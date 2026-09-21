using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>A write/destructive call: the client shows what the model wants to do and the user approves or denies it.</summary>
public sealed class ApprovalInteraction : IToolInteraction
{
    public ToolInteractionKind Kind => ToolInteractionKind.Approval;

    public Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var name = context.Call.ToolName;
        IReadOnlyList<AiChatEvent> events =
        [
            new AiChatPermissionRequired(
                context.ConversationId, context.Request.Id, name,
                context.Tool?.DisplayName ?? name, context.Tool?.Effect ?? ToolEffect.Neutral, context.Call.InputJson),
        ];
        return Task.FromResult(events);
    }
}
