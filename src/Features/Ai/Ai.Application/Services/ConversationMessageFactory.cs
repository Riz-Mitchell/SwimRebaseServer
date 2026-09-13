using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services;

/// <summary>
/// Builds <see cref="ConversationMessage"/> / <see cref="ToolExecutionRequest"/> /
/// <see cref="TokenUsageRecord"/> rows from chat-loop data. Pure construction - no persistence, no I/O.
/// </summary>
public static class ConversationMessageFactory
{
    public static ConversationMessage CreateUserMessage(Guid conversationId, int sequenceNumber, string text) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SequenceNumber = sequenceNumber,
        Role = MessageRole.User,
        Content = text,
        RawContentJson = ChatContentJson.Serialize([new TextContentBlock(text)]),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static ConversationMessage CreateAssistantMessage(
        Guid conversationId, int sequenceNumber, string text, string rawContentJson) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SequenceNumber = sequenceNumber,
        Role = MessageRole.Assistant,
        Content = text,
        RawContentJson = rawContentJson,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static ConversationMessage CreateToolResultMessage(
        Guid conversationId, int sequenceNumber, IReadOnlyList<ToolResultContentBlock> blocks) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SequenceNumber = sequenceNumber,
        Role = MessageRole.Tool,
        Content = string.Join("\n", blocks.Select(b => b.ResultJson)),
        RawContentJson = ChatContentJson.Serialize(blocks),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static ToolExecutionRequest CreateToolExecutionRequest(
        Guid conversationId, Guid userId, Guid assistantMessageId, ToolCallRequestedEvent call) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        UserId = userId,
        AssistantMessageId = assistantMessageId,
        AnthropicToolUseId = call.ToolUseId,
        ToolName = call.ToolName,
        InputJson = call.InputJson,
        Status = ToolExecutionStatus.PendingApproval,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static TokenUsageRecord CreateUsageRecord(
        Guid userId, Guid conversationId, ChatTokenUsage usage, bool wasCompactionCall = false) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ConversationId = conversationId,
        Model = usage.Model,
        InputTokens = usage.InputTokens,
        OutputTokens = usage.OutputTokens,
        CacheCreationInputTokens = usage.CacheCreationInputTokens,
        CacheReadInputTokens = usage.CacheReadInputTokens,
        WasCompactionCall = wasCompactionCall,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
