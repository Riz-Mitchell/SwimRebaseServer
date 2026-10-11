using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ai.Application;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sessions.Application.Services;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;
using Shared.Application;

namespace Ai.Infrastructure.SubAgents;

/// <summary>
/// Runs a swim-board conversion as two model calls, each answered as JSON of a fixed schema. The first reads the board and writes the
/// swimmer's whole questionnaire at once (which lane, kick or swim, ...); the second turns the board and their answers into the session.
/// Where it is up to is read from what is stored - the questionnaire's request row and its answers, then the draft - so every call to
/// <see cref="RunAsync"/> just does the next step. A step that fails ends the conversation without a draft, which the main chat is told about.
/// </summary>
public sealed class SwimBoardConversionDriver(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    ITokenUsageRepository tokenUsage,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IAiChatClient chatClient,
    IAiToolRegistry toolRegistry,
    SessionDraftService drafts,
    SessionService sessions,
    IEffortTermLearning effortTerms,
    ILogger<SwimBoardConversionDriver> logger) : IConversationDriver
{
    private const int QuestionnaireMaxTokens = 4000;
    private const int LoggedTextLength = 1500;
    private const int RecentTitleCount = 10;

    public string Kind => InterviewTools.AgentKind;

    public async IAsyncEnumerable<AiChatEvent> RunAsync(
        Guid userId, Guid conversationId, TurnLoopOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            yield return new AiChatError("Conversation not found.");
            yield break;
        }

        outcome.ParentToolExecutionRequestId = conversation.ParentToolExecutionRequestId;

        if (await drafts.FindLatestForConversationAsync(userId, conversationId, cancellationToken) is not null)
        {
            outcome.TerminalToolCompleted = true;
            yield break;
        }

        var questionnaires = await toolExecutions.ListForConversationAsync(conversationId, InterviewTools.Ask, cancellationToken);
        var opening = await ReadOpeningMessageAsync(conversationId, cancellationToken);
        if (opening is null)
        {
            logger.LogWarning("AI swim board conversation {ConversationId} has no opening message", conversationId);
            outcome.EndedWithoutToolCall = true;
            yield break;
        }

        if (questionnaires.Count == 0)
        {
            await foreach (var chatEvent in AskAsync(userId, conversationId, opening, outcome, cancellationToken))
            {
                yield return chatEvent;
            }

            yield break;
        }

        var answered = questionnaires[^1];
        if (answered.Status != ToolExecutionStatus.Approved)
        {
            yield break;
        }

        await foreach (var chatEvent in ConvertAsync(userId, conversationId, opening, answered, outcome, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    private async IAsyncEnumerable<AiChatEvent> AskAsync(
        Guid userId, Guid conversationId, ChatMessage opening, TurnLoopOutcome outcome, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AiChatTurnStarted(conversationId);

        var request = new ChatTurnRequest(
            [opening], InterviewPrompt.Questionnaire, [],
            MaxTokens: QuestionnaireMaxTokens, PromptCaching: true, Effort: InterviewTools.InterviewEffort, OutputSchema: ConversionSchemas.Questionnaire);
        var call = await CallModelAsync("questionnaire", userId, conversationId, request, cancellationToken);
        if (call.Usage is { } usage)
        {
            yield return usage;
        }

        string? questionnaire = null;
        if (call.Reply is { StopReason: "end_turn" } reply)
        {
            try
            {
                var unknown = await effortTerms.UnknownAsync(userId, EffortQuestions.Read(reply.Text), cancellationToken);
                questionnaire = InterviewQuestions.Compose(reply.Text, unknown);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "AI swim board conversation {ConversationId} questionnaire was not readable: {Text}", conversationId, Truncate(reply.Text));
            }
        }

        if (questionnaire is null)
        {
            outcome.EndedWithoutToolCall = true;
            yield break;
        }

        var toolUseId = $"toolu_{Guid.NewGuid():N}";
        var sequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
        var assistant = ConversationMessageFactory.CreateAssistantMessage(
            conversationId, sequence, string.Empty,
            ChatContentJson.Serialize([new ToolUseContentBlock(toolUseId, InterviewTools.Ask, questionnaire)]));
        conversations.AddMessage(assistant);

        var toolRequest = ConversationMessageFactory.CreateToolExecutionRequest(
            conversationId, userId, assistant.Id, new ToolCallRequestedEvent(toolUseId, InterviewTools.Ask, questionnaire, RequiresApproval: true));
        toolExecutions.Add(toolRequest);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await conversations.TouchLastMessageAtAsync(conversationId, DateTimeOffset.UtcNow, cancellationToken);

        var tool = toolRegistry.Find(InterviewTools.Ask) as IQuestionTool
            ?? throw new InvalidOperationException($"Tool '{InterviewTools.Ask}' is not registered as a question tool.");
        yield return tool.CreateQuestionEvent(conversationId, toolRequest.Id, questionnaire);
    }

    private async IAsyncEnumerable<AiChatEvent> ConvertAsync(
        Guid userId, Guid conversationId, ChatMessage opening, ToolExecutionRequest answered, TurnLoopOutcome outcome,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new AiChatTurnStarted(conversationId);

        var transcript = InterviewTranscript.Build([(answered.InputJson, answered.ResultJson)]);
        await effortTerms.SaveAsync(userId, EffortQuestions.Answered(answered.InputJson, answered.ResultJson), cancellationToken);

        // In the user message rather than the system prompt, so the cached prompt is the same for everyone.
        var recentTitles = await sessions.RecentTitlesAsync(userId, RecentTitleCount, cancellationToken);
        var avoid = recentTitles.Count == 0
            ? string.Empty
            : $"\n\nTheir recent session titles (give this one a clearly different title): {string.Join("; ", recentTitles.Select(t => $"\"{t}\""))}.";

        var messages = new List<ChatMessage>
        {
            opening with
            {
                Content = [.. opening.Content, new TextContentBlock($"What the swimmer told you about their path:\n{transcript}{avoid}\n\nWrite the JSON now.")],
            },
        };

        string? sessionJson = null;
        DraftView? draft = null;
        IReadOnlyList<string> problems = [];

        for (var attempt = 0; attempt < 2 && draft is null; attempt++)
        {
            var request = new ChatTurnRequest(
                messages, InterviewPrompt.Conversion, [],
                MaxTokens: InterviewTools.ConversionMaxTokens, PromptCaching: true, Effort: InterviewTools.InterviewEffort, OutputSchema: ConversionSchemas.Session);
            var call = await CallModelAsync(attempt == 0 ? "conversion" : "conversion-repair", userId, conversationId, request, cancellationToken);
            if (call.Usage is { } usage)
            {
                yield return usage;
            }

            if (call.Reply is not { StopReason: "end_turn" } reply)
            {
                break;
            }

            sessionJson = reply.Text;
            try
            {
                var draftStartedAt = Stopwatch.GetTimestamp();
                draft = await drafts.CreateAsync(userId, conversationId, sessionJson, cancellationToken);
                logger.LogInformation(
                    "AI swim board conversation {ConversationId} draft {DraftId} v{Version} stored with {Sets} sets from {Chars} chars ms={ElapsedMs}",
                    conversationId, draft.Id, draft.Version, draft.Session.Sets.Count, sessionJson.Length,
                    (long)Stopwatch.GetElapsedTime(draftStartedAt).TotalMilliseconds);
            }
            catch (InvalidSessionException ex)
            {
                problems = ex.Problems;
                logger.LogWarning(
                    "AI swim board conversation {ConversationId} draft attempt {Attempt} was rejected: {Problems} input={Input}",
                    conversationId, attempt, string.Join("; ", ex.Problems), Truncate(sessionJson));
                messages =
                [
                    .. messages,
                    new ChatMessage(ChatRole.Assistant, [new TextContentBlock(sessionJson)]),
                    new ChatMessage(ChatRole.User, [new TextContentBlock($"That session is not valid: {string.Join("; ", ex.Problems)}. Write the corrected JSON.")]),
                ];
            }
        }

        if (draft is null || sessionJson is null)
        {
            logger.LogWarning("AI swim board conversation {ConversationId} produced no draft ({Problems})", conversationId, string.Join("; ", problems));
            outcome.EndedWithoutToolCall = true;
            yield break;
        }

        WarnWhenLedgerDisagrees(conversationId, sessionJson, draft.Session.Sets.Count);

        var sequence = await conversations.GetNextSequenceNumberAsync(conversationId, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateAssistantMessage(
            conversationId, sequence, sessionJson, ChatContentJson.Serialize([new TextContentBlock(sessionJson)])));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("AI swim board conversation {ConversationId} conversion reply persisted as message {Sequence}", conversationId, sequence);

        outcome.TerminalToolCompleted = true;
    }

    private async Task<ChatMessage?> ReadOpeningMessageAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var live = await conversations.GetLiveContextMessagesAsync(conversationId, cancellationToken);
        return ChatContextMapper.ToChatMessages(live).FirstOrDefault(m => m.Role == ChatRole.User);
    }

    private sealed record ModelCall(ModelReply? Reply, AiChatUsage? Usage);

    private sealed record ModelReply(string Text, string StopReason);

    /// <summary>One structured-output call: the reply text (JSON of the request's schema), or none when the call failed. Usage is recorded and logged either way it completed.</summary>
    private async Task<ModelCall> CallModelAsync(
        string step, Guid userId, Guid conversationId, ChatTurnRequest request, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        TurnCompletedEvent? completed = null;
        await foreach (var evt in chatClient.StreamTurnAsync(request, cancellationToken))
        {
            switch (evt)
            {
                case TurnCompletedEvent tc:
                    completed = tc;
                    break;
                case ErrorEvent error:
                    logger.LogWarning("AI swim board conversation {ConversationId} {Step} call failed: {Error}", conversationId, step, error.Message);
                    return new ModelCall(null, null);
            }
        }

        if (completed is null)
        {
            logger.LogWarning("AI swim board conversation {ConversationId} {Step} call returned no completion", conversationId, step);
            return new ModelCall(null, null);
        }

        var usage = completed.Usage;
        tokenUsage.Add(ConversationMessageFactory.CreateUsageRecord(userId, conversationId, usage));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AI turn {Kind} conv={ConversationId} step={Step} model={Model} effort={Effort} stop={StopReason} in={InputTokens} out={OutputTokens} " +
            "cacheWrite={CacheWriteTokens} cacheRead={CacheReadTokens} msgs={Messages} schemaChars={SchemaChars} systemChars={SystemChars} ms={ElapsedMs}",
            Kind, conversationId, step, usage.Model, request.Effort ?? "default", completed.StopReason, usage.InputTokens, usage.OutputTokens,
            usage.CacheCreationInputTokens, usage.CacheReadInputTokens, request.Messages.Count, request.OutputSchema?.Length ?? 0,
            request.SystemPrompt?.Length ?? 0, (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

        if (completed.StopReason != "end_turn")
        {
            logger.LogWarning(
                "AI swim board conversation {ConversationId} {Step} ended with stop reason {StopReason} ({OutputTokens} output tokens)",
                conversationId, step, completed.StopReason, usage.OutputTokens);
        }

        var chatUsage = new AiChatUsage(
            conversationId, usage.Model, usage.InputTokens, usage.OutputTokens, usage.CacheCreationInputTokens, usage.CacheReadInputTokens);
        return new ModelCall(new ModelReply(completed.FullText, completed.StopReason), chatUsage);
    }

    /// <summary>The ledger lists every board line with what happened to it; far fewer sets than lines swum is the "only part of the session came back" failure.</summary>
    private void WarnWhenLedgerDisagrees(Guid conversationId, string sessionJson, int setCount)
    {
        try
        {
            using var document = JsonDocument.Parse(sessionJson);
            if (!document.RootElement.TryGetProperty("ledger", out var ledger) || ledger.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var swum = ledger.EnumerateArray().Count(entry =>
                entry.TryGetProperty("outcome", out var outcome) && outcome.GetString() is "swum" or "modified" or "added");
            if (setCount < swum)
            {
                logger.LogWarning(
                    "AI swim board conversation {ConversationId} draft has {Sets} sets for {Swum} ledger lines the swimmer swam",
                    conversationId, setCount, swum);
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string Truncate(string text) => text.Length <= LoggedTextLength ? text : text[..LoggedTextLength] + "...";
}
