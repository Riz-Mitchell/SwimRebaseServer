using Ai.Application.Interfaces;
using Ai.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>A write/destructive call: the client shows what the model wants to do and the user approves or denies it.</summary>
public sealed class ApprovalInteraction(ILogger<ApprovalInteraction>? logger = null) : IToolInteraction
{
    private readonly ILogger<ApprovalInteraction> _logger = logger ?? NullLogger<ApprovalInteraction>.Instance;

    public ToolInteractionKind Kind => ToolInteractionKind.Approval;

    public async Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var name = context.Call.ToolName;
        var preview = context.Tool is IToolPreview previewer
            ? await previewer.PreviewAsync(context.UserId, context.Call.InputJson, cancellationToken)
            : null;
        _logger.LogInformation("[{ClassName}] Asking user {UserId} to approve {ToolName} (request {ToolRequestId}, preview {HasPreview})",
            nameof(ApprovalInteraction), context.UserId, name, context.Request.Id, preview is not null);
        return
        [
            new AiChatPermissionRequired(
                context.ConversationId, context.Request.Id, name,
                context.Tool?.DisplayName ?? name, context.Tool?.Effect ?? ToolEffect.Neutral, context.Call.InputJson, preview),
        ];
    }
}
