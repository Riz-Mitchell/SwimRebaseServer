using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;

namespace Ai.Application.Services;

/// <summary>
/// Records one approve/deny decision and, once every gated (write/destructive) call raised by the
/// same assistant turn has been resolved, executes the approved ones and persists the combined
/// tool_result message.
/// </summary>
public sealed class GatedToolApprovalResolver(
    IToolExecutionRepository toolExecutions,
    IConversationRepository conversations,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IChatCompactionService compaction,
    ToolExecutor toolExecutor)
{
    /// <summary>
    /// Returns null while sibling gated calls from the same turn are still pending. Once every
    /// gated call in the turn has an answer, returns the conversation id so the caller can resume
    /// the model loop.
    /// </summary>
    public async Task<Guid?> ResolveAsync(
        Guid userId, ToolExecutionRequest request, bool approve, string? denialReason, CancellationToken cancellationToken)
    {
        // Everything from here through the tool_result message insert is one transaction. Without
        // it, a crash between the two original SaveChanges calls left the request durably
        // Approved/Denied with no corresponding tool_result ever written - a permanently dangling
        // tool_use block that bricks every future send to the conversation (Anthropic rejects any
        // request whose history has a tool_use with no following tool_result). Wrapping it means a
        // crash mid-resolve rolls the status change back too, leaving the request PendingApproval
        // and safely re-resolvable instead of stuck half-done.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        request.Status = approve ? ToolExecutionStatus.Approved : ToolExecutionStatus.Denied;
        request.DenialReason = denialReason;
        request.ResolvedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var siblings = await toolExecutions.GetByAssistantMessageIdAsync(request.AssistantMessageId, cancellationToken);
        if (siblings.Any(s => s.Status == ToolExecutionStatus.PendingApproval))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var resultBlocks = new List<ToolResultContentBlock>();
        foreach (var sibling in siblings)
        {
            resultBlocks.Add(await ResolveSiblingAsync(userId, sibling, cancellationToken));
        }

        var sequenceNumber = await conversations.GetNextSequenceNumberAsync(request.ConversationId, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateToolResultMessage(request.ConversationId, sequenceNumber, resultBlocks));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await compaction.CompactIfNeededAsync(request.ConversationId, userId, cancellationToken);
        return request.ConversationId;
    }

    private async Task<ToolResultContentBlock> ResolveSiblingAsync(
        Guid userId, ToolExecutionRequest sibling, CancellationToken cancellationToken)
    {
        if (sibling.Status == ToolExecutionStatus.Denied)
        {
            return new ToolResultContentBlock(sibling.AnthropicToolUseId, "The user declined this action.", true);
        }

        var block = await toolExecutor.ExecuteAsync(userId, sibling.AnthropicToolUseId, sibling.ToolName, sibling.InputJson, cancellationToken);
        sibling.Status = block.IsError ? ToolExecutionStatus.Failed : ToolExecutionStatus.Executed;
        sibling.ResultJson = block.ResultJson;
        return block;
    }
}
