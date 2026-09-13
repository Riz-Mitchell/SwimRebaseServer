using System.Text.Json.Serialization;

namespace Ai.Application.Models;

/// <summary>
/// What actually streams to the client over SSE. A thin, client-facing projection of the chat
/// loop's internal <see cref="ChatStreamEvent"/>s plus tool-approval and completion signals -
/// never the raw provider events.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AiChatTextDelta), "text_delta")]
[JsonDerivedType(typeof(AiChatPermissionRequired), "permission_required")]
[JsonDerivedType(typeof(AiChatPermissionResolved), "permission_resolved")]
[JsonDerivedType(typeof(AiChatCompleted), "completed")]
[JsonDerivedType(typeof(AiChatError), "error")]
public abstract record AiChatEvent;

public sealed record AiChatTextDelta(string Text) : AiChatEvent;

public sealed record AiChatPermissionRequired(Guid ConversationId, Guid ToolExecutionRequestId, string ToolName, string InputJson) : AiChatEvent;

public sealed record AiChatPermissionResolved(Guid ConversationId, Guid ToolExecutionRequestId, bool Approved) : AiChatEvent;

public sealed record AiChatCompleted(Guid ConversationId, Guid AssistantMessageId) : AiChatEvent;

public sealed record AiChatError(string Message) : AiChatEvent;
