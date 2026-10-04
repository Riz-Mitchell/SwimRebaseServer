namespace Ai.Application.Models;

/// <summary>One event out of <c>IAiChatClient.StreamTurnAsync</c> - a single provider turn's raw stream.</summary>
public abstract record ChatStreamEvent;

public sealed record TextDeltaEvent(string Text) : ChatStreamEvent;

public sealed record ToolCallRequestedEvent(string ToolUseId, string ToolName, string InputJson, bool RequiresApproval, string? LeadInText = null);

public sealed record ChatTokenUsage(
    string Model, long InputTokens, long OutputTokens, long CacheCreationInputTokens, long CacheReadInputTokens);

public sealed record TurnCompletedEvent(
    string StopReason,
    IReadOnlyList<ToolCallRequestedEvent> ToolCalls,
    string FullText,
    string AssistantRawContentJson,
    ChatTokenUsage Usage) : ChatStreamEvent;

public sealed record ErrorEvent(string Message) : ChatStreamEvent;
