using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Billing.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>
/// Starts a sub-agent: creates its private conversation (opened with the images from the parent's latest image
/// message) and leaves the parent's tool call <see cref="ToolExecutionStatus.InProgress"/>. The child is not run here - the
/// loop reports it through <see cref="TurnLoopOutcome.ChildConversationId"/> and <see cref="ConversationRunner"/> runs it once
/// the parent's turn has finished persisting.
/// </summary>
public sealed class SubAgentInteraction(
    IConversationRepository conversations, IEnumerable<ISubAgent> subAgents, IConversionQuota quota, ILogger<SubAgentInteraction>? logger = null) : IToolInteraction
{
    private readonly ILogger<SubAgentInteraction> _logger = logger ?? NullLogger<SubAgentInteraction>.Instance;

    public ToolInteractionKind Kind => ToolInteractionKind.SubAgent;

    public async Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var agent = subAgents.FirstOrDefault(a => a.ToolName == context.Call.ToolName);
        if (agent is null)
        {
            _logger.LogError("[{ClassName}] No sub-agent is registered for tool '{ToolName}'", nameof(SubAgentInteraction), context.Call.ToolName);
            throw new InvalidOperationException($"No sub-agent is registered for tool '{context.Call.ToolName}'.");
        }

        if (context.Outcome.ChildConversationId is not null)
        {
            _logger.LogWarning("[{ClassName}] Refused a second sub-agent in conversation {ConversationId}: one is already starting", nameof(SubAgentInteraction), context.ConversationId);
            return Fail(context, "Only one of these can run at a time - wait for the first to finish, then call it again if still needed.");
        }

        var images = await FindLatestImagesAsync(context.ConversationId, cancellationToken);
        if (images.Count == 0)
        {
            _logger.LogWarning("[{ClassName}] Cannot start {ToolName} in conversation {ConversationId}: no image is attached", nameof(SubAgentInteraction), context.Call.ToolName, context.ConversationId);
            return Fail(context, "There is no image attached to this conversation to read.");
        }

        var childId = Guid.NewGuid();
        var decision = await quota.TryConsumeAsync(context.UserId, childId, cancellationToken);
        if (!decision.Allowed)
        {
            var q = decision.Quota;
            _logger.LogWarning("[{ClassName}] Refused to start {ToolName} for user {UserId}: weekly conversion quota used ({Used}/{Limit})", nameof(SubAgentInteraction), context.Call.ToolName, context.UserId, q.Used, q.Limit);
            Fail(context, $"The user has used all {q.Limit} of their conversions this week; it resets on {q.ResetsAt:yyyy-MM-dd}. Free accounts can upgrade for more. Tell them so and do not call this tool again.");
            return [new AiChatQuotaExceeded(context.ConversationId, q.Limit, q.Used, q.ResetsAt)];
        }

        var now = DateTimeOffset.UtcNow;
        var child = new Conversation
        {
            Id = childId,
            UserId = context.UserId,
            Title = agent.ConversationTitle,
            Kind = agent.Profile.Kind,
            ParentToolExecutionRequestId = context.Request.Id,
            CreatedAt = now,
            LastMessageAt = now,
        };
        conversations.Add(child);
        conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(child.Id, sequenceNumber: 1, agent.BuildOpeningMessage(context.Call.InputJson), images));

        context.Request.Status = ToolExecutionStatus.InProgress;
        context.Outcome.ChildConversationId = child.Id;

        var displayName = context.Tool?.DisplayName ?? context.Call.ToolName;
        _logger.LogInformation("[{ClassName}] Started sub-agent {ToolName} for user {UserId}: child conversation {ChildConversationId} with {ImageCount} image(s)",
            nameof(SubAgentInteraction), context.Call.ToolName, context.UserId, child.Id, images.Count);
        return [new AiChatSubAgentStarted(context.ConversationId, context.Request.Id, context.Call.ToolUseId, child.Id, displayName)];
    }

    /// <summary>Settles the call right away (nothing for the user to do) - the turn loop notices and closes the turn.</summary>
    private static IReadOnlyList<AiChatEvent> Fail(ToolRaiseContext context, string reason)
    {
        context.Request.Status = ToolExecutionStatus.Failed;
        context.Request.ResultJson = reason;
        context.Request.ResolvedAt = DateTimeOffset.UtcNow;
        return [];
    }

    private async Task<IReadOnlyList<ImageContentBlock>> FindLatestImagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var messages = await conversations.GetLiveContextMessagesAsync(conversationId, cancellationToken);
        foreach (var message in messages.Where(m => m.Role == MessageRole.User).OrderByDescending(m => m.SequenceNumber))
        {
            var images = ChatContentJson.Deserialize(message.RawContentJson).OfType<ImageContentBlock>().ToList();
            if (images.Count > 0)
            {
                return images;
            }
        }

        return [];
    }
}
