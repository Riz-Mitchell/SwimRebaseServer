using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Ai.Infrastructure.Tools;
using Sessions.Application.Metrics;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;

namespace Ai.Infrastructure.SubAgents;

/// <summary>
/// Reads a photo of a swim workout board by asking the swimmer about the path they took, then hands back a draft session. It runs in its own
/// conversation, driven by <see cref="SwimBoardConversionDriver"/> (two model calls, no tool loop); the main chat only ever sees its outcome:
/// what the swimmer said, and the numbered draft - and, when the swimmer gave a date and pool, a save card with the draft already attached.
/// </summary>
public sealed class SwimBoardInterviewAgent(SessionDraftService drafts, IToolExecutionRepository toolExecutions) : ISubAgent
{
    private const string OpeningText = "Board attached.";

    public string ToolName => InterviewTools.ReadSwimBoard;

    public AgentProfile Profile { get; } = new(
        Kind: InterviewTools.AgentKind,
        SystemPrompt: InterviewPrompt.Conversion,
        IncludesSharedTools: false,
        Compaction: false,
        PromptCaching: true,
        Effort: InterviewTools.InterviewEffort);

    public string ConversationTitle => "Swim board import";

    public string? MainChatPromptAddendum =>
        $"When the user attaches a photo of a swim workout board and wants it logged or converted, call {InterviewTools.ReadSwimBoard}. It asks " +
        "the user about the path they swam and returns a DRAFT of it, with numbered sets and computed totals. When the user gave the date and pool " +
        $"it also shows them the draft with a save button: then wait for their decision and do not call {InterviewTools.SaveSession} yourself. " +
        $"Otherwise show them the result briefly and ask if it looks right. To change a draft, call {InterviewTools.EditDraft} with the draft id and " +
        $"small edits - never re-type the whole session. To save one, use {InterviewTools.SaveSession} (it needs the date they swam and the pool " +
        "course - ask if they have not said; the user confirms before it is saved). Once they have declined a save, ask what to change. " +
        "The save button shows the user the date, pool and outline, so a save they approve is confirmed: after one, state the saved date and pool " +
        "plainly and do not say they were unconfirmed. Until it has been saved with that tool, never claim a session was saved. For questions about how much they have swum (a stroke, an intensity, a " +
        $"period), call {MetricsTool.Name} instead of estimating.";

    public string BuildOpeningMessage(string toolInputJson)
    {
        var context = ReadContext(toolInputJson);
        return string.IsNullOrWhiteSpace(context)
            ? OpeningText
            : $"{OpeningText}\nWhat the swimmer already told the assistant: {context}";
    }

    public async Task<string> SummarizeAsync(SubAgentCompletion completion, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindLatestForConversationAsync(completion.UserId, completion.ChildConversationId, cancellationToken)
            ?? throw new InvalidOperationException("The interview finished without a draft.");

        var asked = await ApprovedQuestionnairesAsync(completion.ChildConversationId, cancellationToken);
        var transcript = InterviewTranscript.Build(asked.Select(a => (a.InputJson, a.ResultJson)));
        var offered = SaveAnswers(asked) is { } swum
            ? $"The swimmer said they swam on {swum.PerformedOn:yyyy-MM-dd} in a {swum.Course} pool.\n" +
              $"It is being shown to the user right now with a Save button: wait for their decision and do not call {InterviewTools.SaveSession}.\n\n"
            : string.Empty;

        return $"Draft id: {draft.Id} (version {draft.Version}). This is a DRAFT: it has not been saved to the user's log yet ({InterviewTools.SaveSession} does that).\n" +
               offered + "\n" +
               $"What the swimmer told the interviewer:\n{(transcript.Length == 0 ? "(nothing was asked)" : transcript)}\n\n" +
               $"Draft (edit sets by their [index]):\n{draft.Outline}";
    }

    public async Task<ToolCallRequestedEvent?> FollowUpAsync(SubAgentCompletion completion, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindLatestForConversationAsync(completion.UserId, completion.ChildConversationId, cancellationToken);
        if (draft is null || SaveAnswers(await ApprovedQuestionnairesAsync(completion.ChildConversationId, cancellationToken)) is not { } answers)
        {
            return null;
        }

        var (performedOn, course) = answers;
        var input = JsonSerializer.Serialize(
            new { draftId = draft.Id, performedOn = performedOn.ToString("yyyy-MM-dd"), course },
            ToolJsonOptions.Web);
        return new ToolCallRequestedEvent(
            $"toolu_{Guid.NewGuid():N}", InterviewTools.SaveSession, input, RequiresApproval: true,
            LeadInText: $"Here's your session for {performedOn:yyyy-MM-dd} ({course}). Check it over and save it, or tell me what to change.");
    }

    private async Task<IReadOnlyList<ToolExecutionRequest>> ApprovedQuestionnairesAsync(Guid childConversationId, CancellationToken cancellationToken) =>
        (await toolExecutions.ListForConversationAsync(childConversationId, InterviewTools.Ask, cancellationToken))
        .Where(a => a.Status == ToolExecutionStatus.Approved)
        .ToList();

    /// <summary>The date and pool the swimmer gave, or null unless both are there and valid - the app only offers a one-tap save then.</summary>
    private static (DateOnly PerformedOn, string Course)? SaveAnswers(IReadOnlyList<ToolExecutionRequest> answered)
    {
        if (answered.Count == 0)
        {
            return null;
        }

        var last = answered[^1];
        return InterviewTranscript.SwamOn(last.InputJson, last.ResultJson) is { } date
               && InterviewTranscript.Course(last.InputJson, last.ResultJson) is { } course
            ? (date, course)
            : null;
    }

    private static string? ReadContext(string toolInputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(toolInputJson);
            return document.RootElement.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.String
                ? context.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
