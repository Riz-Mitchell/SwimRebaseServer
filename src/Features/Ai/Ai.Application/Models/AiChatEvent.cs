using System.Text.Json.Serialization;

namespace Ai.Application.Models;

/// <summary>
/// What actually streams to the client over SSE. A thin, client-facing projection of the chat
/// loop's internal <see cref="ChatStreamEvent"/>s plus tool-approval and completion signals -
/// never the raw provider events.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AiChatTextDelta), "text_delta")]
[JsonDerivedType(typeof(AiChatTurnStarted), "turn_started")]
[JsonDerivedType(typeof(AiChatToolStarted), "tool_started")]
[JsonDerivedType(typeof(AiChatToolCompleted), "tool_completed")]
[JsonDerivedType(typeof(AiChatPermissionRequired), "permission_required")]
[JsonDerivedType(typeof(AiChatPermissionResolved), "permission_resolved")]
[JsonDerivedType(typeof(AiChatUsage), "usage")]
[JsonDerivedType(typeof(AiChatCompleted), "completed")]
[JsonDerivedType(typeof(AiChatError), "error")]
public abstract record AiChatEvent;

public sealed record AiChatTextDelta(string Text) : AiChatEvent;

/// <summary>A new model call just started - the client can show a "thinking" indicator until the first text delta or tool event arrives.</summary>
public sealed record AiChatTurnStarted(Guid ConversationId) : AiChatEvent;

/// <summary>A tool is about to run. Fired for both auto-executed read tools and freshly-approved gated ones.</summary>
public sealed record AiChatToolStarted(Guid ConversationId, string ToolUseId, string ToolName, string DisplayName, ToolEffect Effect) : AiChatEvent;

public sealed record AiChatToolCompleted(
    Guid ConversationId, string ToolUseId, string ToolName, string DisplayName, ToolEffect Effect, bool Success, int AffectedCount) : AiChatEvent;

public sealed record AiChatPermissionRequired(
    Guid ConversationId, Guid ToolExecutionRequestId, string ToolName, string DisplayName, ToolEffect Effect, string InputJson) : AiChatEvent;

public sealed record AiChatPermissionResolved(Guid ConversationId, Guid ToolExecutionRequestId, bool Approved) : AiChatEvent;

/// <summary>Token usage for the turn that just completed, so the client can show live usage without a separate poll.</summary>
public sealed record AiChatUsage(
    Guid ConversationId, string Model, long InputTokens, long OutputTokens, long CacheCreationInputTokens, long CacheReadInputTokens) : AiChatEvent;

public sealed record AiChatCompleted(Guid ConversationId, Guid AssistantMessageId) : AiChatEvent;

public sealed record AiChatError(string Message) : AiChatEvent;
