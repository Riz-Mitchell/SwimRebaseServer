using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Ai.Application.Services.ToolInteractions;
using Ai.Domain.Models;
using Ai.Infrastructure.Tools;

namespace SwimRebase.Tests.Ai;

/// <summary>Renaming, archiving (with cascade to a sub-agent tree and auto-denial of pending tool requests), and unarchiving.</summary>
public class ConversationArchivingTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ConversationManagementService CreateService(
        InMemoryConversationRepository conversations, InMemoryToolExecutionRepository toolExecutions) =>
        new(conversations, toolExecutions, new NoOpUnitOfWork());

    [Fact]
    public async Task Rename_updates_the_title()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);
        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = chatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });

        var service = CreateService(conversations, toolExecutions);
        var result = await service.RenameAsync(UserId, chatId, "New title", CancellationToken.None);

        Assert.Equal(ConversationManagementResult.Ok, result);
        Assert.Equal("New title", conversations.Conversations.Single(c => c.Id == chatId).Title);
    }

    [Fact]
    public async Task Rename_rejects_an_empty_title_and_a_conversation_belonging_to_someone_else()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);
        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = chatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });

        var service = CreateService(conversations, toolExecutions);

        Assert.Equal(ConversationManagementResult.InvalidTitle, await service.RenameAsync(UserId, chatId, "   ", CancellationToken.None));
        Assert.Equal(ConversationManagementResult.NotFound, await service.RenameAsync(Guid.NewGuid(), chatId, "Mine now", CancellationToken.None));
    }

    [Fact]
    public async Task Archive_cascades_to_the_whole_sub_agent_tree_and_denies_pending_requests_anywhere_in_it()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);

        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = chatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });

        // Chat -> sub-agent conversation A (via a tool call in the chat) -> nested sub-agent conversation B (via a tool call in A).
        var subAgentToolCallInChat = new ToolExecutionRequest
        {
            Id = Guid.NewGuid(), ConversationId = chatId, UserId = UserId, AssistantMessageId = Guid.NewGuid(),
            ToolName = "read_swim_board", CreatedAt = DateTimeOffset.UtcNow, Status = ToolExecutionStatus.InProgress,
        };
        toolExecutions.Add(subAgentToolCallInChat);

        var subAgentAId = Guid.NewGuid();
        conversations.Add(new Conversation
        {
            Id = subAgentAId, UserId = UserId, Kind = "sub_agent", ParentToolExecutionRequestId = subAgentToolCallInChat.Id,
            CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow,
        });

        var pendingApprovalInSubAgentA = new ToolExecutionRequest
        {
            Id = Guid.NewGuid(), ConversationId = subAgentAId, UserId = UserId, AssistantMessageId = Guid.NewGuid(),
            ToolName = "save_session", CreatedAt = DateTimeOffset.UtcNow, Status = ToolExecutionStatus.PendingApproval,
        };
        toolExecutions.Add(pendingApprovalInSubAgentA);

        var nestedSubAgentToolCall = new ToolExecutionRequest
        {
            Id = Guid.NewGuid(), ConversationId = subAgentAId, UserId = UserId, AssistantMessageId = Guid.NewGuid(),
            ToolName = "some_nested_sub_agent", CreatedAt = DateTimeOffset.UtcNow, Status = ToolExecutionStatus.InProgress,
        };
        toolExecutions.Add(nestedSubAgentToolCall);

        var subAgentBId = Guid.NewGuid();
        conversations.Add(new Conversation
        {
            Id = subAgentBId, UserId = UserId, Kind = "sub_agent", ParentToolExecutionRequestId = nestedSubAgentToolCall.Id,
            CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow,
        });

        var pendingApprovalInSubAgentB = new ToolExecutionRequest
        {
            Id = Guid.NewGuid(), ConversationId = subAgentBId, UserId = UserId, AssistantMessageId = Guid.NewGuid(),
            ToolName = "save_session", CreatedAt = DateTimeOffset.UtcNow, Status = ToolExecutionStatus.PendingApproval,
        };
        toolExecutions.Add(pendingApprovalInSubAgentB);

        var treeIds = await conversations.GetConversationTreeIdsAsync(chatId, CancellationToken.None);
        Assert.Equal(new HashSet<Guid> { chatId, subAgentAId, subAgentBId }, treeIds.ToHashSet());

        var service = CreateService(conversations, toolExecutions);
        var result = await service.ArchiveAsync(UserId, chatId, CancellationToken.None);

        Assert.Equal(ConversationManagementResult.Ok, result);
        Assert.All(conversations.Conversations, c => Assert.True(c.IsArchived));
        Assert.All(conversations.Conversations, c => Assert.NotNull(c.ArchivedAt));

        Assert.Equal(ToolExecutionStatus.Denied, pendingApprovalInSubAgentA.Status);
        Assert.Equal("Conversation archived", pendingApprovalInSubAgentA.DenialReason);
        Assert.NotNull(pendingApprovalInSubAgentA.ResolvedAt);

        Assert.Equal(ToolExecutionStatus.Denied, pendingApprovalInSubAgentB.Status);
        Assert.Equal("Conversation archived", pendingApprovalInSubAgentB.DenialReason);

        // In-progress sub-agent tool calls themselves are left alone - only PendingApproval is auto-denied, same as the existing single-conversation cancel path.
        Assert.Equal(ToolExecutionStatus.InProgress, subAgentToolCallInChat.Status);
        Assert.Equal(ToolExecutionStatus.InProgress, nestedSubAgentToolCall.Status);
    }

    [Fact]
    public async Task Archive_and_unarchive_report_not_found_for_someone_elses_conversation()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);
        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = chatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });

        var service = CreateService(conversations, toolExecutions);
        var otherUserId = Guid.NewGuid();

        Assert.Equal(ConversationManagementResult.NotFound, await service.ArchiveAsync(otherUserId, chatId, CancellationToken.None));
        Assert.Equal(ConversationManagementResult.NotFound, await service.UnarchiveAsync(otherUserId, chatId, CancellationToken.None));
    }

    [Fact]
    public async Task Unarchive_restores_only_the_given_conversation_and_does_not_undo_prior_denials()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);
        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation
        {
            Id = chatId, UserId = UserId, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow,
        });

        var service = CreateService(conversations, toolExecutions);
        var result = await service.UnarchiveAsync(UserId, chatId, CancellationToken.None);

        Assert.Equal(ConversationManagementResult.Ok, result);
        var conversation = conversations.Conversations.Single(c => c.Id == chatId);
        Assert.False(conversation.IsArchived);
        Assert.Null(conversation.ArchivedAt);
    }

    [Fact]
    public async Task ListForUserAsync_excludes_archived_conversations_by_default()
    {
        var conversations = new InMemoryConversationRepository();
        var activeId = Guid.NewGuid();
        var archivedId = Guid.NewGuid();
        conversations.Add(new Conversation { Id = activeId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });
        conversations.Add(new Conversation
        {
            Id = archivedId, UserId = UserId, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow,
        });

        var defaultList = await conversations.ListForUserAsync(UserId, ConversationSortBy.LastMessageAt, true, includeArchived: false, CancellationToken.None);
        Assert.Single(defaultList, c => c.Id == activeId);

        var fullList = await conversations.ListForUserAsync(UserId, ConversationSortBy.LastMessageAt, true, includeArchived: true, CancellationToken.None);
        Assert.Equal(2, fullList.Count);
    }

    [Fact]
    public async Task Sending_a_message_into_an_archived_conversation_is_rejected()
    {
        var conversations = new InMemoryConversationRepository();
        var toolExecutions = new InMemoryToolExecutionRepository(conversations);
        var uploadedImages = new InMemoryUploadedImageRepository();
        var unitOfWork = new NoOpUnitOfWork();
        var registry = new AiToolRegistry([new AskUserQuestionTool()]);
        var profiles = new AgentProfileProvider([]);
        var executor = new ToolExecutor(registry);
        var closer = new ToolTurnCloser(toolExecutions, conversations, registry, unitOfWork, new NoOpCompaction(), executor);
        var resolver = new GatedToolApprovalResolver(closer, toolExecutions, conversations, registry, profiles, []);
        IToolInteraction[] interactions = [new ApprovalInteraction(), new QuestionInteraction()];
        var loop = new ChatTurnLoop(
            conversations, toolExecutions, new InMemoryTokenUsageRepository(), unitOfWork, new ScriptedChatClient(), registry,
            new NoOpCompaction(), executor, closer, profiles, interactions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatTurnLoop>.Instance);
        var runner = new ConversationRunner(loop, resolver, new SubAgentFollowUp(toolExecutions, conversations, registry, [], unitOfWork), conversations, []);
        var orchestrator = new ChatOrchestrator(
            conversations, toolExecutions, uploadedImages, unitOfWork, new NoOpCompaction(), new NoOpAiUserProvisioningService(),
            resolver, runner, registry, new NoOpEventTracker(), new FakeTitler(TitleResult.None), new InMemoryTokenUsageRepository());

        var chatId = Guid.NewGuid();
        conversations.Add(new Conversation
        {
            Id = chatId, UserId = UserId, IsArchived = true, ArchivedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow,
        });

        var events = new List<AiChatEvent>();
        await foreach (var e in orchestrator.SendUserMessageAsync(UserId, chatId, "Hello?", null, CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Contains(events.OfType<AiChatError>(), e => e.Message.Contains("archived"));
        Assert.Empty(conversations.Messages);
    }
}
