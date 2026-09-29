using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Ai.Application.Services.ToolInteractions;
using Ai.Domain.Models;
using Ai.Infrastructure.Tools;

namespace SwimRebase.Tests.Ai;

/// <summary>A turn where the model calls a plain read-only tool and a gated tool together: every tool_use must get a tool_result.</summary>
public class MixedToolTurnTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class LookupTool : IAiTool
    {
        public int Runs { get; private set; }

        public string Name => "lookup";

        public string DisplayName => "Looking something up";

        public ToolEffect Effect => ToolEffect.Neutral;

        public string Description => "read-only";

        public string JsonSchema => """{"type":"object","properties":{}}""";

        public bool RequiresApproval => false;

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new ToolExecutionResult("""{"found":6}""", AffectedCount: 6));
        }
    }

    private sealed class Rig
    {
        public readonly InMemoryConversationRepository Conversations = new();
        public readonly InMemoryToolExecutionRepository ToolExecutions;
        public readonly ScriptedChatClient Client;
        public readonly LookupTool Lookup = new();
        public readonly ConversationRunner Runner;
        public readonly GatedToolApprovalResolver Resolver;
        public readonly Guid ChatId = Guid.NewGuid();

        public Rig(params ScriptedReply[] script)
        {
            ToolExecutions = new InMemoryToolExecutionRepository(Conversations);
            Client = new ScriptedChatClient(script);
            var unitOfWork = new NoOpUnitOfWork();
            var registry = new AiToolRegistry([new AskUserQuestionTool(), Lookup]);
            var profiles = new AgentProfileProvider([]);
            var executor = new ToolExecutor(registry);
            var closer = new ToolTurnCloser(ToolExecutions, Conversations, registry, unitOfWork, new NoOpCompaction(), executor);
            Resolver = new GatedToolApprovalResolver(closer, ToolExecutions, Conversations, registry, profiles, []);
            IToolInteraction[] interactions = [new ApprovalInteraction(), new QuestionInteraction()];
            var loop = new ChatTurnLoop(
                Conversations, ToolExecutions, new InMemoryTokenUsageRepository(), unitOfWork, Client, registry,
                new NoOpCompaction(), executor, closer, profiles, interactions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatTurnLoop>.Instance);
            Runner = new ConversationRunner(loop, Resolver);

            Conversations.Add(new Conversation { Id = ChatId, UserId = UserId, CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow });
            Conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(ChatId, 1, "How many sessions do I have, and which day suits me?"));
        }

        public async Task<List<AiChatEvent>> RunAsync()
        {
            var events = new List<AiChatEvent>();
            await foreach (var e in Runner.RunAsync(UserId, ChatId, CancellationToken.None))
            {
                events.Add(e);
            }

            return events;
        }

        public async Task AnswerAsync(string answer, ToolApprovalResolution resolution)
        {
            var question = ToolExecutions.Requests.Single(r => r.ToolName == AiToolNames.AskUserQuestion);
            await foreach (var _ in Resolver.AnswerAsync(UserId, question, [answer], resolution, CancellationToken.None))
            {
            }
        }
    }

    [Fact]
    public async Task The_plain_call_runs_immediately_and_its_result_waits_for_the_gated_one_to_be_answered()
    {
        var rig = new Rig(
            new ScriptedReply(string.Empty, ("lookup", "{}"), (AiToolNames.AskUserQuestion, """{"question":"Which day?","options":["Mon","Tue"]}""")),
            ScriptedReply.Say("You have 6 sessions; Tuesday it is."));

        var first = await rig.RunAsync();

        Assert.Equal(1, rig.Lookup.Runs);
        Assert.Contains(first.OfType<AiChatToolCompleted>(), t => t.ToolName == "lookup" && t.Success && t.AffectedCount == 6);
        Assert.Single(first.OfType<AiChatQuestionAsked>());
        Assert.DoesNotContain(rig.Conversations.Messages, m => m.Role == MessageRole.Tool);

        var resolution = new ToolApprovalResolution();
        await rig.AnswerAsync("Tue", resolution);
        Assert.Equal(rig.ChatId, resolution.ReadyConversationId);

        // One tool_result message answers BOTH tool_use blocks of the assistant turn, and the plain call was not run a second time.
        var toolMessage = Assert.Single(rig.Conversations.Messages, m => m.Role == MessageRole.Tool);
        var results = ChatContentJson.Deserialize(toolMessage.RawContentJson).OfType<ToolResultContentBlock>().ToList();
        var assistantCalls = ChatContentJson.Deserialize(rig.Conversations.Messages.Single(m => m.Role == MessageRole.Assistant).RawContentJson)
            .OfType<ToolUseContentBlock>().Select(b => b.Id).Order().ToList();
        Assert.Equal(assistantCalls, results.Select(r => r.ToolUseId).Order().ToList());
        Assert.Contains(results, r => r.ResultJson == """{"found":6}""" && !r.IsError);
        Assert.Contains(results, r => r.ResultJson.Contains("Tue"));
        Assert.Equal(1, rig.Lookup.Runs);

        // The UI summary keeps what the plain call reported it affected.
        Assert.Contains("\"affectedCount\":6", toolMessage.Content);

        // And the model gets to continue from there.
        var rest = await rig.RunAsync();
        Assert.Contains(rest.OfType<AiChatTextDelta>(), t => t.Text.Contains("Tuesday"));
    }

    [Fact]
    public async Task A_failed_plain_call_is_reported_as_an_error_result_alongside_the_answer()
    {
        var rig = new Rig(
            new ScriptedReply(string.Empty, ("missing_tool", "{}"), (AiToolNames.AskUserQuestion, """{"question":"Which day?"}""")),
            ScriptedReply.Say("ok"));

        await rig.RunAsync();
        await rig.AnswerAsync("Mon", new ToolApprovalResolution());

        var results = ChatContentJson.Deserialize(rig.Conversations.Messages.Single(m => m.Role == MessageRole.Tool).RawContentJson)
            .OfType<ToolResultContentBlock>().ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.IsError && r.ResultJson.Contains("missing_tool"));
        Assert.Contains(results, r => !r.IsError && r.ResultJson.Contains("Mon"));
    }
}
