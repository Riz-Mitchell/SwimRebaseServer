using Ai.Application.Interfaces;
using Ai.Domain.Models;

namespace Ai.Application.Models;

/// <summary>
/// Everything that makes one kind of conversation behave differently from another while running through the
/// same <c>ChatTurnLoop</c>: its system prompt, which tools it offers, and a few loop policies. The main chat is
/// <see cref="Chat"/>; each <see cref="ISubAgent"/> brings its own.
/// </summary>
/// <param name="Kind">Matches <see cref="Conversation.Kind"/>.</param>
/// <param name="IncludesSharedTools">Whether tools with no <see cref="IAiToolDefinition.ScopedToAgent"/> are offered.</param>
/// <param name="Compaction">Whether long history is folded into a rolling summary.</param>
/// <param name="RequireToolUse">Steers every model turn toward a single tool call - no prose answers (the prompt must say so; the API cannot force it).</param>
/// <param name="PromptCaching">Adds cache breakpoints on the system prompt and the newest message, for conversations that re-read a big prefix every turn.</param>
/// <param name="TerminalToolName">When this tool succeeds the conversation is finished and the loop stops.</param>
/// <param name="MaxQuestionTurns">Cap on question turns; the answer to the last one tells the model to finish.</param>
public sealed record AgentProfile(
    string Kind,
    string SystemPrompt,
    bool IncludesSharedTools,
    bool Compaction = true,
    bool RequireToolUse = false,
    bool PromptCaching = false,
    string? TerminalToolName = null,
    int? MaxQuestionTurns = null,
    string? Model = null,
    int? MaxTokens = null)
{
    public const string ChatSystemPrompt =
        "You are the SwimRebase assistant. You can freely read the current user's own swim data to " +
        "answer questions. Any action that writes, changes, or deletes data always requires the " +
        "user's explicit approval first - never assume it and never claim an action succeeded before " +
        "it has actually been approved and executed.";

    public static AgentProfile Chat(string systemPrompt = ChatSystemPrompt) =>
        new(ConversationKinds.Chat, systemPrompt, IncludesSharedTools: true);

    public bool Includes(IAiToolDefinition tool) =>
        tool.ScopedToAgent is null ? IncludesSharedTools : tool.ScopedToAgent == Kind;
}
