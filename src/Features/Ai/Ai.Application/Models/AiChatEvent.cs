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
[JsonDerivedType(typeof(AiChatQuestionAsked), "question_asked")]
[JsonDerivedType(typeof(AiChatQuestionsAsked), "questions_asked")]
[JsonDerivedType(typeof(AiChatSubAgentStarted), "sub_agent_started")]
[JsonDerivedType(typeof(AiChatSubAgentCompleted), "sub_agent_completed")]
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

/// <summary>The model is asking the user a clarifying question. `Options` are advisory quick-reply suggestions only - the user may always answer with free text.</summary>
public sealed record AiChatQuestionAsked(
    Guid ConversationId, Guid ToolExecutionRequestId, string Question, IReadOnlyList<string> Options) : AiChatEvent;

/// <summary>Token usage for the turn that just completed, so the client can show live usage without a separate poll.</summary>
public sealed record AiChatUsage(
    Guid ConversationId, string Model, long InputTokens, long OutputTokens, long CacheCreationInputTokens, long CacheReadInputTokens) : AiChatEvent;

public sealed record AiChatCompleted(Guid ConversationId, Guid AssistantMessageId) : AiChatEvent;

public sealed record AiChatError(string Message) : AiChatEvent;

public enum AskedQuestionKind
{
    /// <summary>A choice inside one set of questions - several can be asked together.</summary>
    Detail,

    /// <summary>Branches to entirely different follow-up questions, so it is always the last one asked in a turn.</summary>
    Fork,
}

public sealed record AskedQuestion(string Text, AskedQuestionKind Kind, IReadOnlyList<string> Options);

/// <summary>
/// Several questions asked together; the client answers them all at once (one answer per question, in order) via the same
/// answer endpoint. As with <see cref="AiChatQuestionAsked"/>, <c>Options</c> are quick-reply suggestions and the user may type anything.
/// </summary>
public sealed record AiChatQuestionsAsked(
    Guid ConversationId, Guid ToolExecutionRequestId, IReadOnlyList<AskedQuestion> Questions) : AiChatEvent;

/// <summary>
/// A tool call in <c>ParentConversationId</c> started a sub-agent. Every event from here until the matching
/// <see cref="AiChatSubAgentCompleted"/> that carries <c>ChildConversationId</c> belongs to it - render them under the tool call
/// <c>ToolUseId</c>. Answer its questions through the ordinary answer endpoint; cancel it with the cancel endpoint on <c>ToolExecutionRequestId</c>.
/// </summary>
public sealed record AiChatSubAgentStarted(
    Guid ParentConversationId, Guid ToolExecutionRequestId, string ToolUseId, Guid ChildConversationId, string DisplayName) : AiChatEvent;

public sealed record AiChatSubAgentCompleted(
    Guid ParentConversationId, Guid ToolExecutionRequestId, Guid ChildConversationId, string DisplayName, bool Success) : AiChatEvent;
