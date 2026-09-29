using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Ai.Infrastructure.Chat;
using Anthropic.Models.Messages;
using Shared.Application.Storage;

namespace SwimRebase.Tests.Ai;

public class AnthropicChatClientRequestTests
{
    private static readonly AiToolSpec Tool = new("ask", "Ask a question", """{"type":"object","properties":{}}""", false);
    private static readonly IReadOnlyList<ChatMessage> Messages = [new(ChatRole.User, [new TextContentBlock("hi")])];

    private static MessageCreateParams Build(bool requireToolUse, params AiToolSpec[] tools) =>
        AnthropicChatClient.BuildParameters(
            new ChatTurnRequest(Messages, "system", tools, RequireToolUse: requireToolUse),
            new AiModelOptions(),
            null!);

    [Fact]
    public void Required_tool_use_sends_auto_tool_choice_with_parallel_calls_disabled()
    {
        var parameters = Build(requireToolUse: true, Tool);

        Assert.NotNull(parameters.ToolChoice);
        Assert.True(parameters.ToolChoice.TryPickAuto(out var auto));
        Assert.True(auto.DisableParallelToolUse);
    }

    [Fact]
    public void Tool_choice_is_never_forced_because_newer_models_reject_it()
    {
        var parameters = Build(requireToolUse: true, Tool);

        Assert.False(parameters.ToolChoice!.TryPickAny(out _));
        Assert.False(parameters.ToolChoice.TryPickTool(out _));
    }

    [Fact]
    public void Effort_is_sent_when_the_request_sets_it_and_omitted_otherwise()
    {
        var withEffort = AnthropicChatClient.BuildParameters(
            new ChatTurnRequest(Messages, "system", [Tool], Effort: "medium"), new AiModelOptions(), null!);
        var without = Build(requireToolUse: false, Tool);

        Assert.Equal(Effort.Medium, withEffort.OutputConfig!.Effort!.Value());
        Assert.Null(without.OutputConfig);
    }

    [Fact]
    public void Tool_choice_is_omitted_when_tool_use_is_not_required()
    {
        Assert.Null(Build(requireToolUse: false, Tool).ToolChoice);
    }
}
