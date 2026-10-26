using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Infrastructure.Tools;

namespace SwimRebase.Tests.Ai;

public class AskUserQuestionToolTests
{
    private const string Input = """{"question":"Which day?","options":["Mon","Tue"]}""";

    [Fact]
    public void The_question_event_carries_the_question_and_options()
    {
        var conversationId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var asked = Assert.IsType<AiChatQuestionAsked>(new AskUserQuestionTool().CreateQuestionEvent(conversationId, requestId, Input));

        Assert.Equal(conversationId, asked.ConversationId);
        Assert.Equal(requestId, asked.ToolExecutionRequestId);
        Assert.Equal("Which day?", asked.Question);
        Assert.Equal(["Mon", "Tue"], asked.Options);
    }

    [Fact]
    public void The_answer_becomes_the_tool_result_in_the_same_shape_as_before()
    {
        var result = new AskUserQuestionTool().FormatAnswer(Input, ["Tuesday please"], new QuestionAnswerContext(1, null));

        Assert.Equal("""{"answer":"Tuesday please"}""", result);
    }

    [Fact]
    public void A_question_with_no_text_is_rejected_so_the_user_is_never_left_with_nothing_to_answer()
    {
        var tool = new AskUserQuestionTool();

        Assert.Null(tool.Rejection(Input));
        Assert.NotNull(tool.Rejection("""{"question":"  "}"""));
        Assert.NotNull(tool.Rejection("{}"));
        Assert.NotNull(tool.Rejection("""{"question":"Which day?","options":"Mon,Tue"}"""));
    }

    [Fact]
    public void It_is_a_question_that_waits_on_the_user_and_belongs_to_the_main_chat()
    {
        IAiToolDefinition tool = new AskUserQuestionTool();

        Assert.Equal(ToolInteractionKind.Question, tool.Interaction);
        Assert.True(tool.RequiresApproval);
        Assert.Null(tool.ScopedToAgent);
    }
}
