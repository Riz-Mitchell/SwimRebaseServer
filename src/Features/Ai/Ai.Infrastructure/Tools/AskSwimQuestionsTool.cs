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
