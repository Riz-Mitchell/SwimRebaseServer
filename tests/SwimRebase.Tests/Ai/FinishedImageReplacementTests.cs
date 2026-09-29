using Ai.Application.Models;

namespace SwimRebase.Tests.Ai;

public class FinishedImageReplacementTests
{
    private static readonly IReadOnlySet<string> ImageTools = new HashSet<string> { "read_board" };
    private static readonly ImageContentBlock Image = new("boards/a.png", "image/png");

    private static ChatMessage User(params ChatContentBlock[] blocks) => new(ChatRole.User, blocks);
    private static ChatMessage Assistant(params ChatContentBlock[] blocks) => new(ChatRole.Assistant, blocks);

    [Fact]
    public void An_image_is_kept_until_an_image_tool_has_answered()
    {
        List<ChatMessage> history = [User(new TextContentBlock("log this"), Image), Assistant(new ToolUseContentBlock("t1", "read_board", "{}"))];

        var mapped = ChatContextMapper.ReplaceFinishedImages(history, ImageTools);

        Assert.Contains(mapped[0].Content, b => b is ImageContentBlock);
    }

    [Fact]
    public void An_image_becomes_text_once_an_image_tool_has_answered_but_the_history_itself_is_unchanged()
    {
        List<ChatMessage> history =
        [
            User(new TextContentBlock("log this"), Image),
            Assistant(new ToolUseContentBlock("t1", "read_board", "{}")),
            User(new ToolResultContentBlock("t1", "draft", false)),
            Assistant(new TextContentBlock("Looks right?")),
        ];

        var mapped = ChatContextMapper.ReplaceFinishedImages(history, ImageTools);

        Assert.DoesNotContain(mapped[0].Content, b => b is ImageContentBlock);
        Assert.Contains(mapped[0].Content, b => b is TextContentBlock t && t.Text.Contains("Image attached earlier"));
        Assert.Contains(history[0].Content, b => b is ImageContentBlock);
        Assert.Equal(history.Count, mapped.Count);
    }

    [Fact]
    public void A_new_image_after_the_answer_is_kept()
    {
        List<ChatMessage> history =
        [
            User(Image),
            Assistant(new ToolUseContentBlock("t1", "read_board", "{}")),
            User(new ToolResultContentBlock("t1", "draft", false)),
            User(new TextContentBlock("another one"), Image),
        ];

        var mapped = ChatContextMapper.ReplaceFinishedImages(history, ImageTools);

        Assert.DoesNotContain(mapped[0].Content, b => b is ImageContentBlock);
        Assert.Contains(mapped[3].Content, b => b is ImageContentBlock);
    }

    [Fact]
    public void Results_from_other_tools_do_not_release_the_image()
    {
        List<ChatMessage> history =
        [
            User(Image),
            Assistant(new ToolUseContentBlock("t1", "get_metrics", "{}")),
            User(new ToolResultContentBlock("t1", "{}", false)),
        ];

        var mapped = ChatContextMapper.ReplaceFinishedImages(history, ImageTools);

        Assert.Contains(mapped[0].Content, b => b is ImageContentBlock);
    }
}
