using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Application.Services.ToolInteractions;
using Ai.Domain.Models;
using Ai.Infrastructure.Tools;
using Microsoft.Extensions.Options;

namespace SwimRebase.Tests.Ai;

public class ConversationTitleTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static AiConversationTitler Titler(IAiChatClient client, AiTitleOptions? options = null) =>
        new(client, Options.Create(options ?? new AiTitleOptions()));

    // ---- the model call ----

    [Theory]
    [InlineData("Taper plan for 200 fly", "Taper plan for 200 fly")]
    [InlineData("\"Taper plan for 200 fly\"", "Taper plan for 200 fly")]
    [InlineData("“Taper plan for 200 fly.”", "Taper plan for 200 fly")]
    [InlineData("**Taper plan**", "Taper plan")]
    [InlineData("# Taper plan", "Taper plan")]
    [InlineData("Title: Taper plan", "Taper plan")]
    [InlineData("Taper plan!!\nA second line the model added", "Taper plan")]
    [InlineData("  Taper \t plan  ", "Taper plan")]
    [InlineData("", null)]
    [InlineData("   \n  ", null)]
    [InlineData("\"\"", null)]
    [InlineData(null, null)]
    public void A_reply_is_cleaned_into_one_plain_title(string? reply, string? expected)
    {
        Assert.Equal(expected, AiConversationTitler.Sanitize(reply));
    }

    [Fact]
    public void A_long_reply_is_cut_at_a_word_boundary()
    {
        var title = AiConversationTitler.Sanitize(string.Join(' ', Enumerable.Repeat("endurance", 20)));

        Assert.NotNull(title);
        Assert.True(title.Length <= AiConversationTitler.MaxLength);
        Assert.DoesNotContain("endura ", title + " ");
        Assert.EndsWith("endurance", title);
    }

    [Fact]
    public async Task The_call_uses_the_configured_cheap_model_with_no_tools_and_returns_title_and_usage()
    {
        var client = StubChatClient.Replying("Taper plan for 200 fly", model: "haiku-test");
        var options = new AiTitleOptions { Model = "haiku-test", MaxTokens = 33 };

        var result = await Titler(client, options).GenerateAsync("How do I taper for a 200 fly?", hasImages: false, CancellationToken.None);

        Assert.Equal("Taper plan for 200 fly", result.Title);
        Assert.Equal("haiku-test", result.Usage?.Model);
        var request = Assert.Single(client.Requests);
        Assert.Equal("haiku-test", request.Model);
        Assert.Equal(33, request.MaxTokens);
        Assert.Empty(request.Tools);
        Assert.Contains("How do I taper for a 200 fly?", Assert.IsType<TextContentBlock>(Assert.Single(Assert.Single(request.Messages).Content)).Text);
    }

    [Fact]
    public async Task A_photo_with_no_words_is_still_titled_and_the_model_is_told_about_the_image()
    {
        var client = StubChatClient.Replying("Workout board");

        var result = await Titler(client).GenerateAsync(string.Empty, hasImages: true, CancellationToken.None);

        Assert.Equal("Workout board", result.Title);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(Assert.Single(client.Requests).Messages[0].Content)).Text;
        Assert.Contains("attached an image", text);
    }

    [Fact]
    public async Task A_very_long_first_message_is_cut_before_it_is_sent()
    {
        var client = StubChatClient.Replying("Long question");

        await Titler(client).GenerateAsync(new string('a', 20_000), hasImages: false, CancellationToken.None);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(client.Requests[0].Messages[0].Content)).Text;
        Assert.True(text.Length < 1200);
    }

    [Fact]
    public async Task Nothing_is_called_when_titles_are_off_or_there_is_nothing_to_name()
    {
        var client = StubChatClient.Replying("Never used");

        var off = await Titler(client, new AiTitleOptions { Enabled = false }).GenerateAsync("Hello", false, CancellationToken.None);
        var empty = await Titler(client).GenerateAsync("   ", hasImages: false, CancellationToken.None);

        Assert.Null(off.Title);
        Assert.Null(empty.Title);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task A_model_error_a_thrown_exception_or_an_empty_reply_all_leave_no_title_and_never_throw()
    {
        var error = new StubChatClient(_ => StubChatClient.Events(new ErrorEvent("overloaded")));
        var thrown = new StubChatClient(_ => throw new InvalidOperationException("boom"));
        var empty = StubChatClient.Replying("  ");

        Assert.Null((await Titler(error).GenerateAsync("Hello", false, CancellationToken.None)).Title);
        Assert.Null((await Titler(thrown).GenerateAsync("Hello", false, CancellationToken.None)).Title);

        var blank = await Titler(empty).GenerateAsync("Hello", false, CancellationToken.None);
        Assert.Null(blank.Title);
        Assert.NotNull(blank.Usage);
    }

    [Fact]
    public async Task A_model_that_never_answers_is_given_up_on_after_the_timeout()
    {
        var hangs = new StubChatClient(HangUntilCancelled);

        var result = await Titler(hangs, new AiTitleOptions { TimeoutSeconds = 1 }).GenerateAsync("Hello", false, CancellationToken.None);

        Assert.Null(result.Title);
    }

    private static async IAsyncEnumerable<ChatStreamEvent> HangUntilCancelled([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    // ---- the orchestrator ----

    private sealed class Rig
    {
        public readonly InMemoryConversationRepository Conversations = new();
        public readonly InMemoryTokenUsageRepository Usage = new();
        public readonly ChatOrchestrator Orchestrator;

        public Rig(IConversationTitler titler, string reply = "Sure.")
        {
            var toolExecutions = new InMemoryToolExecutionRepository(Conversations);
            var unitOfWork = new NoOpUnitOfWork();
            var registry = new AiToolRegistry([]);
            var profiles = new AgentProfileProvider([]);
            var executor = new ToolExecutor(registry);
            var closer = new ToolTurnCloser(toolExecutions, Conversations, registry, unitOfWork, new NoOpCompaction(), executor);
            var resolver = new GatedToolApprovalResolver(closer, toolExecutions, Conversations, registry, profiles, []);
            IToolInteraction[] interactions = [new ApprovalInteraction(), new QuestionInteraction()];
            var loop = new ChatTurnLoop(
                Conversations, toolExecutions, Usage, unitOfWork, new ScriptedChatClient(ScriptedReply.Say(reply), ScriptedReply.Say(reply)), registry,
                new NoOpCompaction(), executor, closer, profiles, interactions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatTurnLoop>.Instance);
            var runner = new ConversationRunner(loop, resolver, new SubAgentFollowUp(toolExecutions, Conversations, registry, [], unitOfWork), Conversations, []);
            Orchestrator = new ChatOrchestrator(
                Conversations, toolExecutions, new InMemoryUploadedImageRepository(), unitOfWork, new NoOpCompaction(), new NoOpAiUserProvisioningService(),
                resolver, runner, registry, new NoOpEventTracker(), titler, Usage);
        }

        public async Task<List<AiChatEvent>> SendAsync(Guid? conversationId, string message)
        {
            var events = new List<AiChatEvent>();
            await foreach (var e in Orchestrator.SendUserMessageAsync(UserId, conversationId, message, null, CancellationToken.None))
            {
                events.Add(e);
            }

            return events;
        }
    }

    private static TitleResult Titled(string title) => new(title, new ChatTokenUsage("title-model", 20, 6, 0, 0));

    [Fact]
    public async Task A_new_chat_gets_the_generated_title_announced_before_the_turn_ends()
    {
        var titler = new FakeTitler(Titled("Taper plan for 200 fly"));
        var rig = new Rig(titler);

        var events = await rig.SendAsync(null, "How do I taper for a 200 fly?");

        var chat = Assert.Single(rig.Conversations.Conversations);
        Assert.Equal("Taper plan for 200 fly", chat.Title);
        var titleEvent = Assert.Single(events.OfType<AiChatTitleUpdated>());
        Assert.Equal(chat.Id, titleEvent.ConversationId);
        Assert.Equal("Taper plan for 200 fly", titleEvent.Title);
        Assert.True(events.IndexOf(titleEvent) < events.FindIndex(e => e is AiChatCompleted));
        Assert.IsType<AiChatCompleted>(events[^1]);
        Assert.Equal(("How do I taper for a 200 fly?", false), Assert.Single(titler.Calls));
        Assert.Contains(rig.Usage.Records, r => r.Model == "title-model" && !r.WasCompactionCall);
    }

    [Fact]
    public async Task The_chat_shows_the_start_of_the_first_message_until_the_title_is_ready()
    {
        var titler = new FakeTitler(TitleResult.None);
        var rig = new Rig(titler);

        var events = await rig.SendAsync(null, "How do I taper for a 200 fly?");

        Assert.Equal("How do I taper for a 200 fly?", Assert.Single(rig.Conversations.Conversations).Title);
        Assert.Empty(events.OfType<AiChatTitleUpdated>());
        Assert.IsType<AiChatCompleted>(events[^1]);
    }

    [Fact]
    public async Task A_later_message_in_an_existing_chat_is_never_retitled()
    {
        var titler = new FakeTitler(Titled("Something else"));
        var rig = new Rig(titler);
        await rig.SendAsync(null, "First message");
        titler.Calls.Clear();
        var chatId = rig.Conversations.Conversations.Single().Id;
        var titleBefore = rig.Conversations.Conversations.Single().Title;

        var events = await rig.SendAsync(chatId, "Second message");

        Assert.Empty(titler.Calls);
        Assert.Empty(events.OfType<AiChatTitleUpdated>());
        Assert.Equal(titleBefore, rig.Conversations.Conversations.Single().Title);
    }

    [Fact]
    public async Task A_rename_made_while_the_title_was_being_generated_is_not_overwritten()
    {
        Rig? rig = null;
        var titler = new FakeTitler(Titled("Generated title")) { OnGenerate = () => rig!.Conversations.Conversations.Single().Title = "My own name" };
        rig = new Rig(titler);

        var events = await rig.SendAsync(null, "How do I taper for a 200 fly?");

        Assert.Equal("My own name", rig.Conversations.Conversations.Single().Title);
        Assert.Empty(events.OfType<AiChatTitleUpdated>());
    }

    [Fact]
    public async Task A_titler_that_throws_or_faults_never_breaks_the_chat()
    {
        foreach (var titler in new[]
        {
            new FakeTitler(TitleResult.None) { Throws = new InvalidOperationException("sync") },
            new FakeTitler(TitleResult.None) { Faults = new InvalidOperationException("async") },
        })
        {
            var rig = new Rig(titler);

            var events = await rig.SendAsync(null, "How do I taper for a 200 fly?");

            Assert.Empty(events.OfType<AiChatError>());
            Assert.Empty(events.OfType<AiChatTitleUpdated>());
            Assert.IsType<AiChatCompleted>(events[^1]);
            Assert.Equal("How do I taper for a 200 fly?", Assert.Single(rig.Conversations.Conversations).Title);
        }
    }

    [Fact]
    public async Task The_title_is_not_saved_again_when_it_matches_the_one_already_there()
    {
        var rig = new Rig(new FakeTitler(Titled("How do I taper for a 200 fly?")));

        var events = await rig.SendAsync(null, "How do I taper for a 200 fly?");

        Assert.Empty(events.OfType<AiChatTitleUpdated>());
    }
}
