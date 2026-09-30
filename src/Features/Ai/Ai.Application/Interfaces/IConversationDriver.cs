using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <summary>
/// Runs a conversation of one <see cref="Kind"/> in place of the generic <c>ChatTurnLoop</c>, for agents whose work is a fixed
/// sequence of model calls rather than an open-ended tool loop. It reports how the run ended through the same
/// <see cref="TurnLoopOutcome"/>, so <c>ConversationRunner</c> follows the same hand-offs afterwards.
/// </summary>
public interface IConversationDriver
{
    /// <summary>The <see cref="Ai.Domain.Models.Conversation.Kind"/> this driver runs.</summary>
    string Kind { get; }

    IAsyncEnumerable<AiChatEvent> RunAsync(Guid userId, Guid conversationId, TurnLoopOutcome outcome, CancellationToken cancellationToken);
}
