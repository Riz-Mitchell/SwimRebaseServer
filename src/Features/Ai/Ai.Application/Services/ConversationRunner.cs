using System.Runtime.CompilerServices;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Billing.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services;

/// <summary>
/// Runs a conversation through <see cref="ChatTurnLoop"/> and follows the hand-offs that come out of it: a turn that
/// started a sub-agent continues into the sub-agent's conversation, and a sub-agent that finishes settles its parent
/// tool call and continues into the parent. All of it streams as one sequence of events, so the client sees a single chat.
/// A conversation whose kind has an <see cref="IConversationDriver"/> is run by it; every other one goes through the turn loop.
/// </summary>
public sealed class ConversationRunner(
    ChatTurnLoop turnLoop,
    GatedToolApprovalResolver approvalResolver,
    SubAgentFollowUp followUp,
    IConversationRepository conversations,
    IEnumerable<IConversationDriver> drivers,
    ILogger<ConversationRunner>? logger = null,
    IConversionQuota? quota = null)
{
    private readonly ILogger<ConversationRunner> _logger = logger ?? NullLogger<ConversationRunner>.Instance;

    public async IAsyncEnumerable<AiChatEvent> RunAsync(
        Guid userId, Guid conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outcome = new TurnLoopOutcome();
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        var driver = conversation is null ? null : drivers.FirstOrDefault(d => d.Kind == conversation.Kind);
        _logger.LogInformation("[{ClassName}] Running conversation {ConversationId} for user {UserId} via {Runner}",
            nameof(ConversationRunner), conversationId, userId, driver is null ? "the turn loop" : driver.GetType().Name);
        if (conversation is null)
        {
            _logger.LogWarning("[{ClassName}] Conversation {ConversationId} was not found for user {UserId}; falling back to the turn loop", nameof(ConversationRunner), conversationId, userId);
        }

        var events = driver is null
            ? turnLoop.RunAsync(userId, conversationId, outcome, cancellationToken)
            : driver.RunAsync(userId, conversationId, outcome, cancellationToken);
        await foreach (var chatEvent in events)
        {
            yield return chatEvent;
        }

        if (outcome.ChildConversationId is { } childId)
        {
            _logger.LogInformation("[{ClassName}] Conversation {ConversationId} started sub-agent conversation {ChildConversationId}; continuing into it", nameof(ConversationRunner), conversationId, childId);
            await foreach (var chatEvent in RunAsync(userId, childId, cancellationToken))
            {
                yield return chatEvent;
            }

            yield break;
        }

        // This conversation is a sub-agent's and it just ended one way or the other: report back to the parent tool call.
        if (outcome.ParentToolExecutionRequestId is { } parentRequestId
            && (outcome.TerminalToolCompleted || outcome.EndedWithoutToolCall))
        {
            _logger.LogInformation(
                "[{ClassName}] Sub-agent conversation {ConversationId} ended (terminalTool={Terminal}, endedWithoutToolCall={NoToolCall}); settling parent request {ParentRequestId}",
                nameof(ConversationRunner), conversationId, outcome.TerminalToolCompleted, outcome.EndedWithoutToolCall, parentRequestId);
            if (quota is not null && outcome.EndedWithoutToolCall && !outcome.TerminalToolCompleted)
            {
                _logger.LogWarning("[{ClassName}] Sub-agent conversation {ConversationId} ended without a result; refunding its conversion", nameof(ConversationRunner), conversationId);
                await quota.RefundAsync(conversationId, cancellationToken);
            }

            var resolution = new ToolApprovalResolution();
            await foreach (var chatEvent in approvalResolver.CompleteSubAgentAsync(
                userId, conversationId, parentRequestId, outcome.TerminalToolCompleted, resolution, cancellationToken))
            {
                yield return chatEvent;
            }

            if (resolution.ReadyConversationId is { } parentConversationId)
            {
                if (outcome.TerminalToolCompleted
                    && await followUp.RaiseAsync(userId, conversationId, parentRequestId, cancellationToken) is { } followUpEvents)
                {
                    _logger.LogInformation(
                        "[{ClassName}] Parent conversation {ParentConversationId} is waiting on the user: a follow-up request was raised instead of resuming the model",
                        nameof(ConversationRunner), parentConversationId);
                    foreach (var chatEvent in followUpEvents)
                    {
                        yield return chatEvent;
                    }

                    yield break;
                }

                _logger.LogInformation("[{ClassName}] Parent conversation {ParentConversationId} resumes the model after its sub-agent", nameof(ConversationRunner), parentConversationId);
                await foreach (var chatEvent in RunAsync(userId, parentConversationId, cancellationToken))
                {
                    yield return chatEvent;
                }
            }
        }
    }
}
