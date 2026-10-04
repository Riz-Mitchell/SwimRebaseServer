using Ai.Application.Models;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Domain.Models;
using Microsoft.Extensions.Options;

namespace SwimRebase.Tests.Ai;

public class ChatCompactionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ConversationMessage Message(Guid conversationId, int sequence, MessageRole role, string text, params ChatContentBlock[] extra) => new()
    {
        Id = Guid.NewGuid(),
        ConversationId = conversationId,
        SequenceNumber = sequence,
        Role = role,
        Content = text,
        RawContentJson = ChatContentJson.Serialize([new TextContentBlock(text), .. extra]),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static (ChatCompactionService Service, InMemoryConversationRepository Conversations, ScriptedChatClient Client, Guid ChatId) Rig(
        int maxLive, int retain)
    {
        var conversations = new InMemoryConversationRepository();
        var client = new ScriptedChatClient(ScriptedReply.Say("summary"));
        var service = new ChatCompactionService(
            conversations, new InMemoryTokenUsageRepository(), new NoOpUnitOfWork(), client, new AgentProfileProvider([]),
            Options.Create(new ChatCompactionOptions { MaxLiveMessagesBeforeCompaction = maxLive, MessagesToRetainAfterCompaction = retain }));
        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = chatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });
        return (service, conversations, client, chatId);
    }

    [Fact]
    public async Task The_retained_tail_starts_at_a_user_message_so_no_tool_result_is_orphaned_from_its_tool_use()
    {
        var (service, conversations, _, chatId) = Rig(maxLive: 5, retain: 3);
        conversations.AddMessage(Message(chatId, 1, MessageRole.User, "log my swim"));
        conversations.AddMessage(Message(chatId, 2, MessageRole.Assistant, "", new ToolUseContentBlock("t1", "lookup", "{}")));
        conversations.AddMessage(Message(chatId, 3, MessageRole.Tool, "[]", new ToolResultContentBlock("t1", "{}", false)));
        conversations.AddMessage(Message(chatId, 4, MessageRole.Assistant, "done"));
        conversations.AddMessage(Message(chatId, 5, MessageRole.User, "again"));
        conversations.AddMessage(Message(chatId, 6, MessageRole.Assistant, "", new ToolUseContentBlock("t2", "lookup", "{}")));
        conversations.AddMessage(Message(chatId, 7, MessageRole.Tool, "[]", new ToolResultContentBlock("t2", "{}", false)));
        conversations.AddMessage(Message(chatId, 8, MessageRole.Assistant, "ok"));
        conversations.AddMessage(Message(chatId, 9, MessageRole.User, "thanks"));
        conversations.AddMessage(Message(chatId, 10, MessageRole.Assistant, "welcome"));

        await service.CompactIfNeededAsync(chatId, UserId, CancellationToken.None);

        var live = await conversations.GetLiveContextMessagesAsync(chatId, CancellationToken.None);
        Assert.Equal(MessageRole.User, live[0].Role);
        Assert.Equal(5, live[0].SequenceNumber);
        Assert.Equal(4, Assert.Single(conversations.Summaries).CoveredThroughSequenceNumber);
    }

    [Fact]
    public async Task Nothing_is_compacted_when_no_user_message_can_start_the_tail()
    {
        var (service, conversations, client, chatId) = Rig(maxLive: 3, retain: 2);
        conversations.AddMessage(Message(chatId, 1, MessageRole.User, "start"));
        conversations.AddMessage(Message(chatId, 2, MessageRole.Assistant, "", new ToolUseContentBlock("t1", "lookup", "{}")));
        conversations.AddMessage(Message(chatId, 3, MessageRole.Tool, "[]", new ToolResultContentBlock("t1", "{}", false)));
        conversations.AddMessage(Message(chatId, 4, MessageRole.Assistant, "done"));

        await service.CompactIfNeededAsync(chatId, UserId, CancellationToken.None);

        Assert.Empty(conversations.Summaries);
        Assert.Empty(client.Requests);
        Assert.Equal(4, (await conversations.GetLiveContextMessagesAsync(chatId, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task The_summary_input_keeps_what_each_tool_call_was_given()
    {
        var (service, conversations, client, chatId) = Rig(maxLive: 3, retain: 2);
        conversations.AddMessage(Message(chatId, 1, MessageRole.User, "save it"));
        conversations.AddMessage(Message(chatId, 2, MessageRole.Assistant, "", new ToolUseContentBlock("t1", "save_session", """{"performedOn":"2026-09-21","course":"scy"}""")));
        conversations.AddMessage(Message(chatId, 3, MessageRole.Tool, "[]", new ToolResultContentBlock("t1", "{}", false)));
        conversations.AddMessage(Message(chatId, 4, MessageRole.Assistant, "saved"));
        conversations.AddMessage(Message(chatId, 5, MessageRole.User, "thanks"));
        conversations.AddMessage(Message(chatId, 6, MessageRole.Assistant, "welcome"));

        await service.CompactIfNeededAsync(chatId, UserId, CancellationToken.None);

        var transcript = Assert.IsType<TextContentBlock>(Assert.Single(Assert.Single(client.Requests).Messages).Content.Single()).Text;
        Assert.Contains("called save_session with {\"performedOn\":\"2026-09-21\",\"course\":\"scy\"}", transcript);
    }
}
