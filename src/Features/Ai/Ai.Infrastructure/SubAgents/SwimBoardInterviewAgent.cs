using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Sessions.Application.Metrics;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;

namespace Ai.Infrastructure.SubAgents;

/// <summary>
/// Reads a photo of a swim workout board by interviewing the swimmer about the path they took, then hands back a draft session. The
/// interview runs in its own conversation with a small, cached prefix (short prompt, two tools, the board); the main chat only ever sees
/// its outcome: what the swimmer said, and the numbered draft.
/// </summary>
public sealed class SwimBoardInterviewAgent(SessionDraftService drafts, IToolExecutionRepository toolExecutions) : ISubAgent
{
    private const string OpeningText = "Board attached. Begin.";

    public string ToolName => InterviewTools.ReadSwimBoard;

    public AgentProfile Profile { get; } = new(
        Kind: InterviewTools.AgentKind,
        SystemPrompt: InterviewPrompt.System,
        IncludesSharedTools: false,
        Compaction: false,
        RequireToolUse: true,
        PromptCaching: true,
        TerminalToolName: InterviewTools.DraftSession,
        MaxQuestionTurns: InterviewTools.MaxQuestionTurns,
        Effort: InterviewTools.InterviewEffort);

    public string ConversationTitle => "Swim board import";

    public string? MainChatPromptAddendum =>
        $"When the user attaches a photo of a swim workout board and wants it logged or converted, call {InterviewTools.ReadSwimBoard}. It interviews " +
        "the user directly and returns a DRAFT of what they swam, with numbered sets and computed totals. Show them the result briefly and ask if it looks " +
        $"right. To change it, call {InterviewTools.EditDraft} with the draft id and small edits - never re-type the whole session. Once they are happy, " +
        $"save it with {InterviewTools.SaveSession} (it needs the date they swam and the pool course - ask if they have not said; the user confirms before it is saved). " +
        "Until it has been saved with that tool, never claim a session was saved. For questions about how much they have swum (a stroke, an intensity, a " +
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

        var asked = await toolExecutions.ListForConversationAsync(completion.ChildConversationId, InterviewTools.Ask, cancellationToken);
        var transcript = InterviewTranscript.Build(
            asked.Where(a => a.Status == ToolExecutionStatus.Approved).Select(a => (a.InputJson, a.ResultJson)));

        return $"Draft id: {draft.Id} (version {draft.Version}). This is a DRAFT: it has not been saved to the user's log yet ({InterviewTools.SaveSession} does that).\n\n" +
               $"What the swimmer told the interviewer:\n{(transcript.Length == 0 ? "(nothing was asked)" : transcript)}\n\n" +
               $"Draft (edit sets by their [index]):\n{draft.Outline}";
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
