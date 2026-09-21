using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// A single clarifying question with optional quick-reply options. Never "executes": the user's typed answer,
/// recorded through the answer endpoint, becomes the tool_result directly.
/// </summary>
public sealed class AskUserQuestionTool : IQuestionTool
{
    private static readonly JsonSerializerOptions AnswerJsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => AiToolNames.AskUserQuestion;

    public string DisplayName => "Asking a question";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description =>
        "Pauses to ask the user a clarifying question before proceeding. Use this when you need " +
        "information only the user has - never guess or assume when the answer materially changes " +
        "what you'd do next. `options` are optional quick-reply suggestions; the user can always " +
        "answer with free text instead, so keep them short and genuinely mutually exclusive.";

    public bool RequiresApproval => true;

    public ToolInteractionKind Interaction => ToolInteractionKind.Question;

    public string JsonSchema => """
        {
          "type": "object",
          "properties": {
            "question": { "type": "string", "description": "The question to ask the user." },
            "options": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Optional short quick-reply suggestions. Not exhaustive - the user may answer with anything."
            }
          },
          "required": ["question"]
        }
        """;

    public AiChatEvent CreateQuestionEvent(Guid conversationId, Guid toolExecutionRequestId, string inputJson)
    {
        var input = AskUserQuestionInput.Parse(inputJson);
        return new AiChatQuestionAsked(conversationId, toolExecutionRequestId, input.Question, input.Options);
    }

    public string FormatAnswer(string inputJson, IReadOnlyList<string> answers, QuestionAnswerContext context) =>
        JsonSerializer.Serialize(new { answer = answers.FirstOrDefault() ?? string.Empty }, AnswerJsonOptions);
}
