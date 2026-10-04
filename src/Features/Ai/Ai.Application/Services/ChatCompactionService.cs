using System.Text;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>
/// Keeps a conversation's live context bounded. Once <see cref="ChatCompactionOptions.MaxLiveMessagesBeforeCompaction"/>
/// is crossed, folds the oldest messages (everything except the last
/// <see cref="ChatCompactionOptions.MessagesToRetainAfterCompaction"/>) into a rolling summary via a
/// cheap model call, and marks them compacted. Subsequent turns send: latest summary + live tail only -
/// token usage per turn stays roughly constant no matter how long the conversation runs.
/// </summary>
public sealed class ChatCompactionService(
    IConversationRepository conversations,
    ITokenUsageRepository tokenUsage,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IAiChatClient chatClient,
    IAgentProfileProvider profiles,
    IOptions<ChatCompactionOptions> options) : IChatCompactionService
{
    private const string SummarizationSystemPrompt =
        "You maintain a running briefing for yourself covering an ongoing chat between a swimmer and " +
        "an AI assistant. Fold the new messages into the existing summary (if any). Preserve concrete " +
        "facts - times, dates, goals, names, decisions, anything the assistant committed to - and drop " +
        "small talk and pleasantries. Write dense prose, not a transcript. Output only the updated summary.";

    public async Task CompactIfNeededAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null || !profiles.Get(conversation.Kind).Compaction)
        {
            return;
        }

        var settings = options.Value;
        var liveMessages = await conversations.GetLiveContextMessagesAsync(conversationId, cancellationToken);
        if (liveMessages.Count <= settings.MaxLiveMessagesBeforeCompaction)
        {
            return;
        }

        var ordered = liveMessages.OrderBy(m => m.SequenceNumber).ToList();
        var toCompactCount = CleanCutIndex(ordered, ordered.Count - settings.MessagesToRetainAfterCompaction);
        if (toCompactCount <= 0)
        {
            return;
        }

        var toCompact = ordered.Take(toCompactCount).ToList();
        var priorSummary = await conversations.GetLatestSummaryAsync(conversationId, cancellationToken);
        var summaryText = await SummarizeAsync(userId, conversationId, priorSummary, toCompact, settings, cancellationToken);

        var summary = new ConversationSummary
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Content = summaryText,
            CoveredThroughSequenceNumber = toCompact[^1].SequenceNumber,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        conversations.AddSummary(summary);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await conversations.MarkMessagesCompactedAsync(toCompact.Select(m => m.Id).ToList(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<string> SummarizeAsync(
        Guid userId,
        Guid conversationId,
        ConversationSummary? priorSummary,
        IReadOnlyList<ConversationMessage> messages,
        ChatCompactionOptions settings,
        CancellationToken cancellationToken)
    {
        var transcript = BuildTranscript(priorSummary, messages);
        var summaryText = new StringBuilder();

        await foreach (var evt in chatClient.StreamTurnAsync(
            new ChatTurnRequest(
                Messages: [new ChatMessage(ChatRole.User, [new TextContentBlock(transcript)])],
                SystemPrompt: SummarizationSystemPrompt,
                Tools: [],
                Model: settings.SummarizationModel,
                MaxTokens: settings.SummarizationMaxTokens),
            cancellationToken))
        {
            switch (evt)
            {
                case TextDeltaEvent delta:
                    summaryText.Append(delta.Text);
                    break;
                case TurnCompletedEvent completed:
                    tokenUsage.Add(ConversationMessageFactory.CreateUsageRecord(userId, conversationId, completed.Usage, wasCompactionCall: true));
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    break;
            }
        }

        return summaryText.ToString();
    }

    private static string BuildTranscript(ConversationSummary? priorSummary, IReadOnlyList<ConversationMessage> messages)
    {
        var sb = new StringBuilder();
        if (priorSummary is not null)
        {
            sb.AppendLine("Existing summary of earlier context:").AppendLine(priorSummary.Content).AppendLine();
        }

        sb.AppendLine("Messages to fold in:");
        foreach (var message in messages)
        {
            sb.AppendLine($"{message.Role}: {message.Content}");
            foreach (var toolUse in ChatContentJson.Deserialize(message.RawContentJson).OfType<ToolUseContentBlock>())
            {
                sb.AppendLine($"  called {toolUse.ToolName} with {toolUse.InputJson}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The retained tail must start at a user's own text message: starting at a tool result would orphan it from its tool_use, and
    /// starting at an assistant message breaks the alternation the API requires. Moves the cut earlier (compacting less) until it does;
    /// returns 0 when no such message exists.
    /// </summary>
    private static int CleanCutIndex(IReadOnlyList<ConversationMessage> ordered, int cut)
    {
        for (var i = Math.Min(cut, ordered.Count - 1); i > 0; i--)
        {
            if (ordered[i].Role == MessageRole.User)
            {
                return i;
            }
        }

        return 0;
    }
}
