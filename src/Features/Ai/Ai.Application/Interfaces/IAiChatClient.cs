using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <summary>
/// A single-provider-call abstraction over the model. Implemented in Ai.Infrastructure against the
/// Anthropic SDK - Ai.Application never references Anthropic types directly. Streams exactly one
/// model turn; looping across tool calls is the orchestrator's job, not this client's.
/// </summary>
public interface IAiChatClient
{
    IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken);
}

/// <summary>A tool description surfaced to the model. <see cref="RequiresApproval"/> drives the write-guard.</summary>
public sealed record AiToolSpec(string Name, string Description, string JsonSchema, bool RequiresApproval);

/// <summary>
/// <paramref name="Model"/> and <paramref name="MaxTokens"/> override the client's configured
/// defaults (<c>AiModelOptions</c>) for this call only - used by compaction to run its
/// summarization turn on a cheaper model than the user-facing chat model.
/// <paramref name="PromptCaching"/> adds cache breakpoints on the system prompt and the newest message so a
/// long, growing conversation re-reads its prefix from the provider's cache. <paramref name="RequireToolUse"/>
/// asks the model to call at most one tool this turn instead of answering in prose (the system prompt has to
/// say so; the provider no longer lets us force it). <paramref name="OutputSchema"/> is a JSON Schema the reply text is constrained to, so the
/// model's whole answer is JSON of that shape (no recursion, every object closed - the provider's structured-output rules).
/// </summary>
public sealed record ChatTurnRequest(
    IReadOnlyList<ChatMessage> Messages,
    string? SystemPrompt,
    IReadOnlyList<AiToolSpec> Tools,
    string? Model = null,
    int? MaxTokens = null,
    bool PromptCaching = false,
    bool RequireToolUse = false,
    string? Effort = null,
    string? OutputSchema = null);
