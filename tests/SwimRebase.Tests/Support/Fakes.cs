using System.Runtime.CompilerServices;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using SessionConversion.Application.Interfaces;
using SessionConversion.Domain.Models;
using Shared.Application;

namespace SwimRebase.Tests;

internal sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IUnitOfWorkTransaction>(new NoOpTransaction());

    private sealed class NoOpTransaction : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class InMemoryDraftRepository : ISessionDraftRepository
{
    public List<SessionDraft> Drafts { get; } = [];

    public void Add(SessionDraft draft) => Drafts.Add(draft);

    public Task<SessionDraft?> FindAsync(Guid userId, Guid draftId, CancellationToken cancellationToken) =>
        Task.FromResult(Drafts.FirstOrDefault(d => d.Id == draftId && d.UserId == userId));

    public Task<SessionDraft?> FindLatestForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(Drafts.Where(d => d.UserId == userId && d.SourceConversationId == conversationId).OrderByDescending(d => d.CreatedAt).FirstOrDefault());
}

internal sealed class InMemoryConversationRepository : IConversationRepository
{
    public List<Conversation> Conversations { get; } = [];

    public List<ConversationMessage> Messages { get; } = [];

    /// <summary>Wired up by <see cref="InMemoryToolExecutionRepository"/>'s constructor so tree-walking has access to both lists.</summary>
    public List<ToolExecutionRequest> ToolExecutionRequests { get; set; } = [];

    public Task<Conversation?> FindAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.FirstOrDefault(c => c.Id == conversationId && c.UserId == userId));

    public Task<Conversation?> FindChildAsync(Guid userId, Guid parentToolExecutionRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(Conversations.FirstOrDefault(c => c.UserId == userId && c.ParentToolExecutionRequestId == parentToolExecutionRequestId));

    public Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, ConversationSortBy sortBy, bool descending, bool includeArchived, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Conversation>>(
            Conversations.Where(c => c.UserId == userId && c.Kind == ConversationKinds.Chat && (includeArchived || !c.IsArchived)).ToList());

    public Task<IReadOnlyList<Guid>> GetConversationTreeIdsAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var allIds = new HashSet<Guid> { conversationId };
        var frontier = new List<Guid> { conversationId };

        while (frontier.Count > 0)
        {
            var childIds = Conversations
                .Where(c => c.ParentToolExecutionRequestId != null
                    && frontier.Any(f => ToolExecutionRequests.Any(r => r.Id == c.ParentToolExecutionRequestId && r.ConversationId == f))
                    && !allIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();

            if (childIds.Count == 0) break;

            allIds.UnionWith(childIds);
            frontier = childIds;
        }

        return Task.FromResult<IReadOnlyList<Guid>>(allIds.ToList());
    }

    public void Add(Conversation conversation) => Conversations.Add(conversation);

    public Task<IReadOnlyList<ConversationMessage>> GetLiveContextMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConversationMessage>>(
            Messages.Where(m => m.ConversationId == conversationId && !m.IsCompacted).OrderBy(m => m.SequenceNumber).ToList());

    public Task<IReadOnlyList<ConversationMessage>> GetMessagesPageAsync(Guid conversationId, int? beforeSequenceNumber, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ConversationSummary?> GetLatestSummaryAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<ConversationSummary?>(null);

    public Task<int> GetNextSequenceNumberAsync(Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(Messages.Where(m => m.ConversationId == conversationId).Select(m => m.SequenceNumber).DefaultIfEmpty(0).Max() + 1);

    public void AddMessage(ConversationMessage message) => Messages.Add(message);

    public void AddSummary(ConversationSummary summary) => throw new NotSupportedException();

    public Task MarkMessagesCompactedAsync(IReadOnlyList<Guid> messageIds, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task TouchLastMessageAtAsync(Guid conversationId, DateTimeOffset at, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ArchiveAsync(IReadOnlyList<Guid> conversationIds, DateTimeOffset at, CancellationToken cancellationToken)
    {
        foreach (var conversation in Conversations.Where(c => conversationIds.Contains(c.Id)))
        {
            conversation.IsArchived = true;
            conversation.ArchivedAt = at;
        }

        return Task.CompletedTask;
    }

    public Task UnarchiveAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (conversation is not null)
        {
            conversation.IsArchived = false;
            conversation.ArchivedAt = null;
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryToolExecutionRepository : IToolExecutionRepository
{
    private readonly InMemoryConversationRepository _conversations;

    public InMemoryToolExecutionRepository(InMemoryConversationRepository conversations)
    {
        _conversations = conversations;
        conversations.ToolExecutionRequests = Requests;
    }

    public List<ToolExecutionRequest> Requests { get; } = [];

    public void Add(ToolExecutionRequest request) => Requests.Add(request);

    public Task<ToolExecutionRequest?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.FirstOrDefault(r => r.Id == id && r.UserId == userId));

    public Task<IReadOnlyList<ToolExecutionRequest>> GetByAssistantMessageIdAsync(Guid assistantMessageId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ToolExecutionRequest>>(Requests.Where(r => r.AssistantMessageId == assistantMessageId).ToList());

    public Task<IReadOnlyList<ToolExecutionRequest>> GetPendingForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        bool InFamily(Guid id) =>
            id == conversationId
            || _conversations.Conversations.Any(c => c.Id == id
                && Requests.Any(parent => parent.Id == c.ParentToolExecutionRequestId && parent.ConversationId == conversationId));

        return Task.FromResult<IReadOnlyList<ToolExecutionRequest>>(
            Requests.Where(r => r.UserId == userId && r.Status is ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress && InFamily(r.ConversationId)).OrderBy(r => r.CreatedAt).ToList());
    }

    public Task<bool> HasOutstandingRequestAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.Any(r =>
            r.UserId == userId && r.ConversationId == conversationId
            && r.Status is ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress));

    public Task<int> CountForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.Count(r => r.ConversationId == conversationId && r.ToolName == toolName));

    public Task<IReadOnlyList<ToolExecutionRequest>> ListForConversationAsync(Guid conversationId, string toolName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ToolExecutionRequest>>(
            Requests.Where(r => r.ConversationId == conversationId && r.ToolName == toolName).OrderBy(r => r.CreatedAt).ToList());

    public Task DenyOutstandingForConversationAsync(Guid conversationId, string reason, CancellationToken cancellationToken)
    {
        foreach (var request in Requests.Where(r => r.ConversationId == conversationId && r.Status == ToolExecutionStatus.PendingApproval))
        {
            request.Status = ToolExecutionStatus.Denied;
            request.DenialReason = reason;
            request.ResolvedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task DenyOutstandingForConversationsAsync(IReadOnlyList<Guid> conversationIds, string reason, CancellationToken cancellationToken)
    {
        foreach (var request in Requests.Where(r => conversationIds.Contains(r.ConversationId) && r.Status == ToolExecutionStatus.PendingApproval))
        {
            request.Status = ToolExecutionStatus.Denied;
            request.DenialReason = reason;
            request.ResolvedAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTokenUsageRepository : ITokenUsageRepository
{
    public List<TokenUsageRecord> Records { get; } = [];

    public void Add(TokenUsageRecord record) => Records.Add(record);

    public Task<TokenUsageSummary> GetSummaryForUserAsync(Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class NoOpCompaction : IChatCompactionService
{
    public Task CompactIfNeededAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoOpEventTracker : IEventTracker
{
    public Task TrackAsync(Guid userId, string eventType, string? metadataJson, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class NoOpAiUserProvisioningService : IAiUserProvisioningService
{
    public Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class InMemoryUploadedImageRepository : IUploadedImageRepository
{
    public List<UploadedImage> Images { get; } = [];

    public void Add(UploadedImage image) => Images.Add(image);

    public void Remove(UploadedImage image) => Images.Remove(image);

    public Task<UploadedImage?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Images.FirstOrDefault(i => i.Id == id && i.UserId == userId));
}

/// <summary>What the scripted model does on one turn: optional prose, then zero or more tool calls.</summary>
internal sealed record ScriptedReply(string Text, params (string Name, string InputJson)[] ToolCalls)
{
    public static ScriptedReply Say(string text) => new(text);

    public static ScriptedReply Call(string name, string inputJson) => new(string.Empty, (name, inputJson));
}

/// <summary>Plays the model: each request gets the next scripted reply, and every request is kept so tests can inspect what was sent.</summary>
internal sealed class ScriptedChatClient(params ScriptedReply[] replies) : IAiChatClient
{
    private readonly Queue<ScriptedReply> _replies = new(replies);
    private int _toolUseCounter;

    public List<ChatTurnRequest> Requests { get; } = [];

    public int Remaining => _replies.Count;

    public async IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(ChatTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var reply = _replies.Count > 0 ? _replies.Dequeue() : throw new InvalidOperationException("The scripted model was asked for more turns than were scripted.");

        var blocks = new List<ChatContentBlock>();
        if (reply.Text.Length > 0)
        {
            blocks.Add(new TextContentBlock(reply.Text));
        }

        var calls = new List<ToolCallRequestedEvent>();
        foreach (var (name, inputJson) in reply.ToolCalls)
        {
            var id = $"toolu_{++_toolUseCounter}";
            blocks.Add(new ToolUseContentBlock(id, name, inputJson));
            calls.Add(new ToolCallRequestedEvent(id, name, inputJson, request.Tools.FirstOrDefault(t => t.Name == name)?.RequiresApproval ?? false));
        }

        if (reply.Text.Length > 0)
        {
            yield return new TextDeltaEvent(reply.Text);
        }

        yield return new TurnCompletedEvent(
            calls.Count > 0 ? "tool_use" : "end_turn", calls, reply.Text, ChatContentJson.Serialize(blocks),
            new ChatTokenUsage("scripted", InputTokens: 10, OutputTokens: 5, CacheCreationInputTokens: 0, CacheReadInputTokens: 0));

        await Task.CompletedTask;
    }
}
