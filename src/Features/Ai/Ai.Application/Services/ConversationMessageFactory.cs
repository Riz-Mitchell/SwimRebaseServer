using System.Text.Json;
using System.Text.Json.Serialization;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services;

/// <summary>
/// Builds <see cref="ConversationMessage"/> / <see cref="ToolExecutionRequest"/> /
/// <see cref="TokenUsageRecord"/> rows from chat-loop data. Pure construction - no persistence, no I/O.
/// </summary>
public static class ConversationMessageFactory
{
    public static ConversationMessage CreateUserMessage(
        Guid conversationId, int sequenceNumber, string text, IReadOnlyList<ImageContentBlock>? images = null)
    {
        var blocks = new List<ChatContentBlock> { new TextContentBlock(text) };
        if (images is { Count: > 0 })
        {
            blocks.AddRange(images);
        }

        return new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            SequenceNumber = sequenceNumber,
            Role = MessageRole.User,
            Content = text,
            RawContentJson = ChatContentJson.Serialize(blocks),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

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

    /// <summary>
    /// <see cref="ConversationMessage.RawContentJson"/> stays the pure provider-protocol blocks (never
    /// touched by UI concerns, so replaying history back to Anthropic can't be affected); <see cref="ConversationMessage.Content"/>
    /// becomes a UI-friendly JSON array of <paramref name="summaries"/> - name/display name/effect/count per call.
    /// </summary>
    public static ConversationMessage CreateToolResultMessage(
        Guid conversationId, int sequenceNumber, IReadOnlyList<ToolResultSummary> summaries) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SequenceNumber = sequenceNumber,
        Role = MessageRole.Tool,
        Content = JsonSerializer.Serialize(summaries, ToolResultSummaryJsonOptions),
        RawContentJson = ChatContentJson.Serialize(summaries.Select(s => (ChatContentBlock)s.ToContentBlock()).ToList()),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static readonly JsonSerializerOptions ToolResultSummaryJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
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
