using System.Runtime.CompilerServices;
using Ai.Application.Models;

namespace Ai.Application.Services;

/// <summary>
/// Runs a conversation through <see cref="ChatTurnLoop"/> and follows the hand-offs that come out of it: a turn that
/// started a sub-agent continues into the sub-agent's conversation, and a sub-agent that finishes settles its parent
/// tool call and continues into the parent. All of it streams as one sequence of events, so the client sees a single chat.
/// </summary>
public sealed class ConversationRunner(ChatTurnLoop turnLoop, GatedToolApprovalResolver approvalResolver)
{
    public async IAsyncEnumerable<AiChatEvent> RunAsync(
        Guid userId, Guid conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outcome = new TurnLoopOutcome();
        await foreach (var chatEvent in turnLoop.RunAsync(userId, conversationId, outcome, cancellationToken))
        {
            yield return chatEvent;
        }

        if (outcome.ChildConversationId is { } childId)
        {
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
            var resolution = new ToolApprovalResolution();
            await foreach (var chatEvent in approvalResolver.CompleteSubAgentAsync(
                userId, conversationId, parentRequestId, outcome.TerminalToolCompleted, resolution, cancellationToken))
            {
                yield return chatEvent;
            }

            if (resolution.ReadyConversationId is { } parentConversationId)
            {
                await foreach (var chatEvent in RunAsync(userId, parentConversationId, cancellationToken))
                {
                    yield return chatEvent;
                }
            }
        }
    }
}
