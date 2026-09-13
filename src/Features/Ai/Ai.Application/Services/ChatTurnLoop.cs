using System.Runtime.CompilerServices;
using System.Text;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services;

/// <summary>
/// The core model loop: stream one turn, persist it, then either auto-execute read tools and loop
/// again, pause on write tools (one <see cref="AiChatPermissionRequired"/> per gated call), or
/// finish. Both <c>ChatOrchestrator</c> entry points resume here once their own setup is done.
/// </summary>
public sealed class ChatTurnLoop(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    ITokenUsageRepository tokenUsage,
    IUnitOfWork unitOfWork,
    IAiChatClient chatClient,
    IAiToolRegistry toolRegistry,
    IChatCompactionService compaction,
    ToolExecutor toolExecutor)
{
    private const int MaxLoopIterations = 8;

    private const string DefaultSystemPrompt =
        "You are the SwimRebase assistant. You can freely read the current user's own swim data to " +
        "answer questions. Any action that writes, changes, or deletes data always requires the " +
        "user's explicit approval first - never assume it and never claim an action succeeded before " +
        "it has actually been approved and executed.";

    public async IAsyncEnumerable<AiChatEvent> RunAsync(
        Guid userId, Guid conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < MaxLoopIterations; iteration++)
        {
            var summary = await conversations.GetLatestSummaryAsync(conversationId, cancellationToken);
            var liveMessages = await conversations.GetLiveContextMessagesAsync(conversationId, cancellationToken);
            var chatMessages = ChatContextMapper.ToChatMessages(liveMessages);
            var tools = toolRegistry.GetAll()
                .Select(t => new AiToolSpec(t.Name, t.Description, t.JsonSchema, t.RequiresApproval))
                .ToList();

            var textBuffer = new StringBuilder();
            TurnCompletedEvent? completed = null;
            var faulted = false;

            await foreach (var evt in chatClient.StreamTurnAsync(
                new ChatTurnRequest(chatMessages, BuildSystemPrompt(summary), tools), cancellationToken))
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

            var assistantSequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
            var assistantMessage = ConversationMessageFactory.CreateAssistantMessage(
                conversationId, assistantSequence, textBuffer.ToString(), completed.AssistantRawContentJson);
            conversations.AddMessage(assistantMessage);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (completed.ToolCalls.Count == 0)
            {
                yield return new AiChatCompleted(conversationId, assistantMessage.Id);
                yield break;
            }

            var gated = completed.ToolCalls.Where(c => c.RequiresApproval).ToList();
            if (gated.Count > 0)
            {
                foreach (var call in gated)
                {
                    var request = ConversationMessageFactory.CreateToolExecutionRequest(conversationId, userId, assistantMessage.Id, call);
                    toolExecutions.Add(request);
                    yield return new AiChatPermissionRequired(conversationId, request.Id, call.ToolName, call.InputJson);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                // Stop entirely, even if some of this turn's calls were read-only - the model needs
                // every tool_use in the turn answered together on the next request.
                yield break;
            }

            var resultBlocks = new List<ToolResultContentBlock>();
            foreach (var call in completed.ToolCalls)
            {
                resultBlocks.Add(await toolExecutor.ExecuteAsync(userId, call.ToolUseId, call.ToolName, call.InputJson, cancellationToken));
            }

            var toolResultSequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
            conversations.AddMessage(ConversationMessageFactory.CreateToolResultMessage(conversationId, toolResultSequence, resultBlocks));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            await compaction.CompactIfNeededAsync(conversationId, userId, cancellationToken);
            // Loop again - the tool results just persisted are picked up as live context above.
        }

        yield return new AiChatError("Stopped after too many tool-use iterations.");
    }

    private static string BuildSystemPrompt(ConversationSummary? summary) => summary is null
        ? DefaultSystemPrompt
        : $"{DefaultSystemPrompt}\n\nSummary of earlier context in this conversation:\n{summary.Content}";
}
