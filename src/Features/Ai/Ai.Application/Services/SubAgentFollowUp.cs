using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>
/// Puts a gated call straight to the user when a sub-agent has just finished (<see cref="ISubAgent.FollowUpAsync"/>), instead of waking the
/// main chat's model to decide what to do next. The call is recorded as if the model had made it - an assistant message holding the tool_use, and
/// its pending request - so approving or declining it later settles the turn like any other gated call.
/// </summary>
public sealed class SubAgentFollowUp(
    IToolExecutionRepository toolExecutions,
    IConversationRepository conversations,
    IAiToolRegistry toolRegistry,
    IEnumerable<ISubAgent> subAgents,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    /// <summary>The events that ask the user, or null when the sub-agent has no follow-up and the main chat should carry on.</summary>
    public async Task<IReadOnlyList<AiChatEvent>?> RaiseAsync(
        Guid userId, Guid childConversationId, Guid parentRequestId, CancellationToken cancellationToken)
    {
        var parent = await toolExecutions.FindAsync(userId, parentRequestId, cancellationToken);
        var agent = parent is null ? null : subAgents.FirstOrDefault(a => a.ToolName == parent.ToolName);
        if (parent is null || agent is null)
        {
            return null;
        }

        var call = await agent.FollowUpAsync(new SubAgentCompletion(userId, childConversationId, parent), cancellationToken);
        var tool = call is null ? null : toolRegistry.Find(call.ToolName);
        if (call is null || tool is null || !call.RequiresApproval)
        {
            return null;
        }

        var conversationId = parent.ConversationId;
        var text = call.LeadInText ?? "Here's your session. Check it over and save it, or tell me what to change.";
        var sequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
        var assistant = ConversationMessageFactory.CreateAssistantMessage(
            conversationId, sequence, text,
            ChatContentJson.Serialize([new TextContentBlock(text), new ToolUseContentBlock(call.ToolUseId, call.ToolName, call.InputJson)]));
        conversations.AddMessage(assistant);

        var request = ConversationMessageFactory.CreateToolExecutionRequest(conversationId, userId, assistant.Id, call);
        toolExecutions.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await conversations.TouchLastMessageAtAsync(conversationId, DateTimeOffset.UtcNow, cancellationToken);

        var preview = tool is IToolPreview previewer ? await previewer.PreviewAsync(userId, call.InputJson, cancellationToken) : null;
        return
        [
            new AiChatTextDelta(text),
            new AiChatPermissionRequired(conversationId, request.Id, call.ToolName, tool.DisplayName, tool.Effect, call.InputJson, preview),
            new AiChatCompleted(conversationId, assistant.Id),
        ];
    }
}
