using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>
/// Starts a sub-agent: creates its private conversation (opened with the images from the parent's latest image
/// message) and leaves the parent's tool call <see cref="ToolExecutionStatus.InProgress"/>. The child is not run here - the
/// loop reports it through <see cref="TurnLoopOutcome.ChildConversationId"/> and <see cref="ConversationRunner"/> runs it once
/// the parent's turn has finished persisting.
/// </summary>
public sealed class SubAgentInteraction(IConversationRepository conversations, IEnumerable<ISubAgent> subAgents) : IToolInteraction
{
    public ToolInteractionKind Kind => ToolInteractionKind.SubAgent;

    public async Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var agent = subAgents.FirstOrDefault(a => a.ToolName == context.Call.ToolName)
            ?? throw new InvalidOperationException($"No sub-agent is registered for tool '{context.Call.ToolName}'.");

        if (context.Outcome.ChildConversationId is not null)
        {
            return Fail(context, "Only one of these can run at a time - wait for the first to finish, then call it again if still needed.");
        }

        var images = await FindLatestImagesAsync(context.ConversationId, cancellationToken);
        if (images.Count == 0)
        {
            return Fail(context, "There is no image attached to this conversation to read.");
        }

        var now = DateTimeOffset.UtcNow;
        var child = new Conversation
        {
            Id = Guid.NewGuid(),
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
