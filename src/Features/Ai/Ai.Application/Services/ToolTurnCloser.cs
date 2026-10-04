using System.Runtime.CompilerServices;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>Out-parameter substitute for an async-iterator method: set once a resolve finishes enumerating.</summary>
public sealed class ToolApprovalResolution
{
    public Guid? ReadyConversationId { get; set; }
}

/// <summary>
/// The shared tail of every way a gated call gets settled (approve/deny, answered question, finished or cancelled
/// sub-agent): apply the outcome to the request, and once every gated call raised by the same assistant turn has been
/// settled, execute the approved ones and persist the combined tool_result message so the model loop can resume.
/// </summary>
public sealed class ToolTurnCloser(
    IToolExecutionRepository toolExecutions,
    IConversationRepository conversations,
    IAiToolRegistry toolRegistry,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IChatCompactionService compaction,
    ToolExecutor toolExecutor)
{
    private const string ApprovedPrefix = "The user reviewed this and approved it. Result: ";

    /// <summary>
    /// <paramref name="settle"/> applies the outcome (status, result, reason) to <paramref name="request"/>. Streams an
    /// <see cref="AiChatToolStarted"/>/<see cref="AiChatToolCompleted"/> pair for each approved sibling actually executed.
    /// <paramref name="resolution"/>.ReadyConversationId stays null while sibling gated calls from the same turn are still
    /// outstanding, and is set once the turn is fully settled so the caller can resume the model loop.
    /// </summary>
    public async IAsyncEnumerable<AiChatEvent> ResolveAsync(
        Guid userId, ToolExecutionRequest request, Action<ToolExecutionRequest> settle,
        ToolApprovalResolution resolution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Everything from here through the tool_result message insert is one transaction. Without
        // it, a crash between the two original SaveChanges calls left the request durably
        // settled with no corresponding tool_result ever written - a permanently dangling
        // tool_use block that bricks every future send to the conversation (Anthropic rejects any
        // request whose history has a tool_use with no following tool_result). Wrapping it means a
        // crash mid-resolve rolls the status change back too, leaving the request outstanding and
        // safely re-resolvable instead of stuck half-done.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        settle(request);
        request.ResolvedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var siblings = await toolExecutions.GetByAssistantMessageIdAsync(request.AssistantMessageId, cancellationToken);
        if (siblings.Any(s => s.Status is ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress))
        {
            await transaction.CommitAsync(cancellationToken);
            resolution.ReadyConversationId = null;
            yield break;
        }

        await foreach (var chatEvent in ExecuteResolvedSiblingsAsync(userId, request, siblings, cancellationToken))
        {
            yield return chatEvent;
        }

        await transaction.CommitAsync(cancellationToken);
        await compaction.CompactIfNeededAsync(request.ConversationId, userId, cancellationToken);
        resolution.ReadyConversationId = request.ConversationId;
    }

    private async IAsyncEnumerable<AiChatEvent> ExecuteResolvedSiblingsAsync(
        Guid userId, ToolExecutionRequest request, IReadOnlyList<ToolExecutionRequest> siblings,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var summaries = new List<ToolResultSummary>();
        foreach (var sibling in siblings)
        {
            var siblingTool = toolRegistry.Find(sibling.ToolName);
            var displayName = siblingTool?.DisplayName ?? sibling.ToolName;
            var effect = siblingTool?.Effect ?? ToolEffect.Neutral;

            if (sibling.Status == ToolExecutionStatus.Denied)
            {
                var declined = string.IsNullOrWhiteSpace(sibling.DenialReason)
                    ? "The user declined this action."
                    : $"The user declined this action. Reason: {sibling.DenialReason}";
                summaries.Add(new ToolResultSummary(sibling.AnthropicToolUseId, sibling.ToolName, displayName, effect, Success: false, AffectedCount: 0, declined));
                continue;
            }

            // Already settled with its result stashed in ResultJson: a question's answer, a sub-agent's summary, the reason it
            // couldn't run, or a plain call the turn loop executed ahead of the gated ones - nothing left to execute.
            if (siblingTool?.Interaction is ToolInteractionKind.Question or ToolInteractionKind.SubAgent
                || sibling.Status is ToolExecutionStatus.Executed or ToolExecutionStatus.Failed)
            {
                var succeeded = sibling.Status != ToolExecutionStatus.Failed;
                summaries.Add(new ToolResultSummary(
                    sibling.AnthropicToolUseId, sibling.ToolName, displayName, effect, succeeded,
                    sibling.AffectedCount ?? (succeeded ? 1 : 0), sibling.ResultJson ?? string.Empty));
                continue;
            }

            yield return new AiChatToolStarted(request.ConversationId, sibling.AnthropicToolUseId, sibling.ToolName, displayName, effect);

            var summary = await toolExecutor.ExecuteAsync(
                userId, sibling.ConversationId, sibling.AnthropicToolUseId, sibling.ToolName, sibling.InputJson, cancellationToken);
            sibling.Status = summary.Success ? ToolExecutionStatus.Executed : ToolExecutionStatus.Failed;
            sibling.ResultJson = summary.ResultJson;
            summaries.Add(summary.Success && siblingTool is { RequiresApproval: true }
                ? summary with { ResultJson = $"{ApprovedPrefix}{summary.ResultJson}" }
                : summary);

            yield return new AiChatToolCompleted(
                request.ConversationId, sibling.AnthropicToolUseId, sibling.ToolName, displayName, effect, summary.Success, summary.AffectedCount);
        }

        var sequenceNumber = await conversations.GetNextSequenceNumberAsync(request.ConversationId, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateToolResultMessage(request.ConversationId, sequenceNumber, summaries));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
