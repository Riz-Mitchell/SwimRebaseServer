using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using SessionConversion.Application.Interview;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// The interviewer's way of asking the swimmer several questions at once. Like <see cref="AskUserQuestionTool"/> it never executes:
/// the swimmer's answers, recorded through the answer endpoint, become the tool_result.
/// </summary>
public sealed class AskSwimQuestionsTool : IQuestionTool
{
    public string Name => InterviewTools.Ask;

    public string DisplayName => "Asking about your swim";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description => InterviewTools.AskDescription;

    public string JsonSchema => InterviewTools.AskSchema;

    public bool RequiresApproval => true;

    public ToolInteractionKind Interaction => ToolInteractionKind.Question;

    public string? ScopedToAgent => InterviewTools.AgentKind;

    public string? Rejection(string inputJson)
    {
        try
        {
            return InterviewQuestions.Asked(inputJson).Count > 0
                ? null
                : "No questions received: `questions` must be a JSON array of {q, kind, opts} objects, not a string. If the path is complete call draft_session; otherwise ask again.";
        }
        catch (JsonException)
        {
            return "The input was not valid JSON. Ask again with `questions` as a JSON array of {q, kind, opts} objects.";
        }
    }

    public AiChatEvent CreateQuestionEvent(Guid conversationId, Guid toolExecutionRequestId, string inputJson) =>
        new AiChatQuestionsAsked(
            conversationId,
            toolExecutionRequestId,
            InterviewQuestions.Asked(inputJson)
                .Select(q => new AskedQuestion(q.Text, q.IsFork ? AskedQuestionKind.Fork : AskedQuestionKind.Detail, q.Options))
                .ToList());

    public string FormatAnswer(string inputJson, IReadOnlyList<string> answers, QuestionAnswerContext context) =>
        InterviewQuestions.FormatAnswer(inputJson, answers, context.IsLastChance);
}
