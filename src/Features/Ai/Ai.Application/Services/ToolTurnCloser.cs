using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    ToolExecutor toolExecutor,
    ILogger<ToolTurnCloser>? logger = null)
{
    private readonly ILogger<ToolTurnCloser> _logger = logger ?? NullLogger<ToolTurnCloser>.Instance;

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
        var phase = "begin transaction";
        var finished = false;
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

            phase = "settle request";
            settle(request);
            request.ResolvedAt = DateTimeOffset.UtcNow;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            phase = "load sibling requests";
            var siblings = await toolExecutions.GetByAssistantMessageIdAsync(request.AssistantMessageId, cancellationToken);
            if (siblings.Any(s => s.Status is ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress))
            {
                phase = "commit (siblings still pending)";
                await transaction.CommitAsync(cancellationToken);
                resolution.ReadyConversationId = null;
                finished = true;
                _logger.LogInformation(
                    "AI tool turn conv={ConversationId} request={RequestId} tool={Tool} settled as {Status}; {Pending} sibling(s) still outstanding ms={ElapsedMs}",
                    request.ConversationId, request.Id, request.ToolName, request.Status,
                    siblings.Count(s => s.Status is ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress),
                    (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                yield break;
            }

            phase = "execute siblings and write tool_result";
            await foreach (var chatEvent in ExecuteResolvedSiblingsAsync(userId, request, siblings, cancellationToken))
            {
                yield return chatEvent;
            }

            phase = "commit";
            await transaction.CommitAsync(cancellationToken);

            phase = "compaction";
            await compaction.CompactIfNeededAsync(request.ConversationId, userId, cancellationToken);
            resolution.ReadyConversationId = request.ConversationId;
            finished = true;
            _logger.LogInformation(
                "AI tool turn conv={ConversationId} request={RequestId} tool={Tool} settled as {Status}; turn closed with {Siblings} tool result(s) ms={ElapsedMs}",
                request.ConversationId, request.Id, request.ToolName, request.Status, siblings.Count,
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        finally
        {
            if (!finished)
            {
                _logger.LogWarning(
                    "AI tool turn conv={ConversationId} request={RequestId} tool={Tool} did not finish: stopped in phase '{Phase}' " +
                    "(status={Status}, cancelled={Cancelled}) ms={ElapsedMs}",
                    request.ConversationId, request.Id, request.ToolName, request.Status, phase,
                    cancellationToken.IsCancellationRequested, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
        }
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

            var executeStartedAt = Stopwatch.GetTimestamp();
            var summary = await toolExecutor.ExecuteAsync(
                userId, sibling.ConversationId, sibling.AnthropicToolUseId, sibling.ToolName, sibling.InputJson, cancellationToken);
            _logger.LogInformation(
                "AI tool {Tool} {Outcome} after approval conv={ConversationId} request={RequestId} resultChars={ResultChars} ms={ElapsedMs}",
                sibling.ToolName, summary.Success ? "ok" : "FAILED", sibling.ConversationId, sibling.Id, summary.ResultJson.Length,
                (long)Stopwatch.GetElapsedTime(executeStartedAt).TotalMilliseconds);
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
