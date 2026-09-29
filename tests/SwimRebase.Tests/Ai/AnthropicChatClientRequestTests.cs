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

    private sealed class StubBlobStorage : IBlobStorageService
    {
        public PresignedUploadUrl CreatePresignedUploadUrl(string objectKey, string contentType, TimeSpan expiry) => throw new NotSupportedException();

        public string CreatePresignedDownloadUrl(string objectKey, TimeSpan expiry) => $"https://blob/{objectKey}";

        public Task<BlobMetadata?> TryGetMetadataAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static IReadOnlyList<ContentBlockParam> Blocks(MessageParam message) => ((IEnumerable<ContentBlockParam>)message.Content.Value!).ToList();

    private static MessageCreateParams BuildCached(params ChatMessage[] messages) =>
        AnthropicChatClient.BuildParameters(
            new ChatTurnRequest(messages, "system", [], PromptCaching: true), new AiModelOptions(), new StubBlobStorage());

    [Fact]
    public void The_newest_message_gets_a_cache_breakpoint_on_its_last_text_block()
    {
        var parameters = BuildCached(new ChatMessage(ChatRole.User, [new TextContentBlock("hi")]));

        Assert.True(Blocks(parameters.Messages[^1])[^1].TryPickText(out var text));
        Assert.NotNull(text.CacheControl);
    }

    [Fact]
    public void The_breakpoint_goes_on_a_trailing_image_or_tool_result_and_only_on_the_newest_message()
    {
        var withImage = BuildCached(
            new ChatMessage(ChatRole.User, [new TextContentBlock("first")]),
            new ChatMessage(ChatRole.Assistant, [new TextContentBlock("ok")]),
            new ChatMessage(ChatRole.User, [new TextContentBlock("board"), new ImageContentBlock("boards/a.png", "image/png")]));
        var withResult = BuildCached(new ChatMessage(ChatRole.User, [new ToolResultContentBlock("t1", "done", false)]));

        Assert.True(Blocks(withImage.Messages[^1])[^1].TryPickImage(out var image));
        Assert.NotNull(image.CacheControl);
        Assert.True(Blocks(withImage.Messages[0])[^1].TryPickText(out var earlier));
        Assert.Null(earlier.CacheControl);
        Assert.True(Blocks(withResult.Messages[^1])[^1].TryPickToolResult(out var result));
        Assert.NotNull(result.CacheControl);
    }

    [Fact]
    public void No_message_breakpoint_is_added_when_prompt_caching_is_off()
    {
        var parameters = AnthropicChatClient.BuildParameters(
            new ChatTurnRequest(Messages, "system", []), new AiModelOptions(), new StubBlobStorage());

        Assert.True(Blocks(parameters.Messages[^1])[^1].TryPickText(out var text));
        Assert.Null(text.CacheControl);
    }
}
