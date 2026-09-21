using System.Runtime.CompilerServices;
using System.Text;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>
/// The core model loop: stream one turn, persist it, then either auto-execute read tools and loop
/// again, pause on gated calls (approval, a question, or a sub-agent - one request row per call), or
/// finish. What differs between the main chat and a sub-agent's conversation - system prompt, tool set,
/// loop policies - comes from the conversation's <see cref="AgentProfile"/>.
/// <see cref="ConversationRunner"/> is the entry point that chains loops together (parent, child, parent again).
/// </summary>
public sealed class ChatTurnLoop(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    ITokenUsageRepository tokenUsage,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IAiChatClient chatClient,
    IAiToolRegistry toolRegistry,
    IChatCompactionService compaction,
    ToolExecutor toolExecutor,
    ToolTurnCloser turnCloser,
    IAgentProfileProvider profiles,
    IEnumerable<IToolInteraction> interactions)
{
    private const int MaxLoopIterations = 8;

    public async IAsyncEnumerable<AiChatEvent> RunAsync(
        Guid userId, Guid conversationId, TurnLoopOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            yield return new AiChatError("Conversation not found.");
            yield break;
        }

        var profile = profiles.Get(conversation.Kind);
        outcome.ParentToolExecutionRequestId = conversation.ParentToolExecutionRequestId;

        for (var iteration = 0; iteration < MaxLoopIterations; iteration++)
        {
            var summary = await conversations.GetLatestSummaryAsync(conversationId, cancellationToken);
            var liveMessages = await conversations.GetLiveContextMessagesAsync(conversationId, cancellationToken);
            var chatMessages = ChatContextMapper.ToChatMessages(liveMessages);
            var hasImage = chatMessages.Any(m => m.Content.Any(b => b is ImageContentBlock));
            var tools = toolRegistry.GetAll()
                .Where(profile.Includes)
                .Where(t => t.Requires != ToolRequirement.ImageAttachment || hasImage)
                .Select(t => new AiToolSpec(t.Name, t.Description, t.JsonSchema, t.RequiresApproval))
                .ToList();

            var textBuffer = new StringBuilder();
            TurnCompletedEvent? completed = null;
            var faulted = false;

            yield return new AiChatTurnStarted(conversationId);

            await foreach (var evt in chatClient.StreamTurnAsync(
                new ChatTurnRequest(
                    chatMessages, BuildSystemPrompt(profile, summary), tools,
                    Model: profile.Model, MaxTokens: profile.MaxTokens,
                    PromptCaching: profile.PromptCaching, RequireToolUse: profile.RequireToolUse),
                cancellationToken))
            {
                switch (evt)
                {
                    case TextDeltaEvent delta:
                        textBuffer.Append(delta.Text);
                        yield return new AiChatTextDelta(delta.Text);
                        break;
                    case TurnCompletedEvent tc:
                        completed = tc;
                        break;
                    case ErrorEvent err:
                        yield return new AiChatError(err.Message);
                        faulted = true;
                        break;
                }
            }

            if (faulted)
            {
                yield break;
            }

            if (completed is null)
            {
                yield return new AiChatError("The model returned no completion.");
                yield break;
            }

            tokenUsage.Add(ConversationMessageFactory.CreateUsageRecord(userId, conversationId, completed.Usage));
            yield return new AiChatUsage(
                conversationId,
                completed.Usage.Model,
                completed.Usage.InputTokens,
                completed.Usage.OutputTokens,
                completed.Usage.CacheCreationInputTokens,
                completed.Usage.CacheReadInputTokens);

            var assistantSequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
            var assistantMessage = ConversationMessageFactory.CreateAssistantMessage(
                conversationId, assistantSequence, textBuffer.ToString(), completed.AssistantRawContentJson);
            conversations.AddMessage(assistantMessage);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await conversations.TouchLastMessageAtAsync(conversationId, DateTimeOffset.UtcNow, cancellationToken);

            if (completed.ToolCalls.Count == 0)
            {
                outcome.EndedWithoutToolCall = true;
                yield return new AiChatCompleted(conversationId, assistantMessage.Id);
                yield break;
            }

            var gated = completed.ToolCalls.Where(c => c.RequiresApproval).ToList();
            if (gated.Count > 0)
            {
                // A turn can mix gated calls with plain (read-only) ones, and the model needs every tool_use in the turn answered
                // together. The plain ones are safe to run right now; record them as already-settled requests alongside the gated
                // ones so whichever call settles the turn last folds everyone's result into the one tool_result message.
                foreach (var call in completed.ToolCalls.Where(c => !c.RequiresApproval))
                {
                    var plainTool = toolRegistry.Find(call.ToolName);
                    var plainName = plainTool?.DisplayName ?? call.ToolName;
                    var plainEffect = plainTool?.Effect ?? ToolEffect.Neutral;
                    yield return new AiChatToolStarted(conversationId, call.ToolUseId, call.ToolName, plainName, plainEffect);

                    var plainSummary = await toolExecutor.ExecuteAsync(userId, conversationId, call.ToolUseId, call.ToolName, call.InputJson, cancellationToken);
                    var settledRequest = ConversationMessageFactory.CreateToolExecutionRequest(conversationId, userId, assistantMessage.Id, call);
                    settledRequest.Status = plainSummary.Success ? ToolExecutionStatus.Executed : ToolExecutionStatus.Failed;
                    settledRequest.ResultJson = plainSummary.ResultJson;
                    settledRequest.AffectedCount = plainSummary.AffectedCount;
                    settledRequest.ResolvedAt = DateTimeOffset.UtcNow;
                    toolExecutions.Add(settledRequest);

                    yield return new AiChatToolCompleted(
                        conversationId, call.ToolUseId, call.ToolName, plainName, plainEffect, plainSummary.Success, plainSummary.AffectedCount);
                }

                var raised = new List<ToolExecutionRequest>();
                foreach (var call in gated)
                {
                    var request = ConversationMessageFactory.CreateToolExecutionRequest(conversationId, userId, assistantMessage.Id, call);
                    toolExecutions.Add(request);
                    raised.Add(request);

                    var tool = toolRegistry.Find(call.ToolName);
                    var interaction = interactions.First(i => i.Kind == (tool?.Interaction ?? ToolInteractionKind.Approval));
                    var events = await interaction.RaiseAsync(new ToolRaiseContext(userId, conversationId, call, request, tool, outcome), cancellationToken);
                    foreach (var raisedEvent in events)
                    {
                        yield return raisedEvent;
                    }
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                if (raised.All(r => r.Status is not (ToolExecutionStatus.PendingApproval or ToolExecutionStatus.InProgress)))
                {
                    // Every gated call was settled as it was raised (e.g. a sub-agent that could not start), so there
                    // is nothing for the user to do - close the turn ourselves and let the model see the results.
                    var settled = new ToolApprovalResolution();
                    await foreach (var closeEvent in turnCloser.ResolveAsync(userId, raised[0], _ => { }, settled, cancellationToken))
                    {
                        yield return closeEvent;
                    }

                    if (settled.ReadyConversationId is not null)
                    {
                        continue;
                    }
                }

                // Stop here: the model resumes once every gated call has been settled and the whole turn's results are written together.
                yield break;
            }

            var summaries = new List<ToolResultSummary>();
            foreach (var call in completed.ToolCalls)
            {
                var callTool = toolRegistry.Find(call.ToolName);
                var callDisplayName = callTool?.DisplayName ?? call.ToolName;
                var callEffect = callTool?.Effect ?? ToolEffect.Neutral;
                yield return new AiChatToolStarted(conversationId, call.ToolUseId, call.ToolName, callDisplayName, callEffect);

                var toolSummary = await toolExecutor.ExecuteAsync(userId, conversationId, call.ToolUseId, call.ToolName, call.InputJson, cancellationToken);
                summaries.Add(toolSummary);

                yield return new AiChatToolCompleted(
                    conversationId, call.ToolUseId, call.ToolName, callDisplayName, callEffect, toolSummary.Success, toolSummary.AffectedCount);
            }

            var toolResultSequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
            conversations.AddMessage(ConversationMessageFactory.CreateToolResultMessage(conversationId, toolResultSequence, summaries));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await conversations.TouchLastMessageAtAsync(conversationId, DateTimeOffset.UtcNow, cancellationToken);

            if (profile.TerminalToolName is { } terminal && summaries.Any(s => s.ToolName == terminal && s.Success))
            {
                outcome.TerminalToolCompleted = true;
                yield break;
            }

            await compaction.CompactIfNeededAsync(conversationId, userId, cancellationToken);
            // Loop again - the tool results just persisted are picked up as live context above.
        }

        yield return new AiChatError("Stopped after too many tool-use iterations.");
    }

    private static string BuildSystemPrompt(AgentProfile profile, ConversationSummary? summary) => summary is null
        ? profile.SystemPrompt
        : $"{profile.SystemPrompt}\n\nSummary of earlier context in this conversation:\n{summary.Content}";
}
