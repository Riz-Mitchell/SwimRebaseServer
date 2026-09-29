using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Ai.Application.Services.ToolInteractions;
using Ai.Domain.Models;
using Ai.Infrastructure.SubAgents;
using Ai.Infrastructure.Tools;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;
using Sessions.Domain.Models;
using SwimRebase.Tests.SessionConversion;

namespace SwimRebase.Tests.Ai;

/// <summary>
/// Drives the real turn loop, runner, resolver and interactions - with in-memory storage and a scripted model - through a swim-board
/// interview nested inside a chat, the way the orchestrator's endpoints would.
/// </summary>
public class SwimBoardFlowTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private const string LaneQuestion = """{"questions":[{"q":"Which lane?","kind":"fork","opts":["Lane 1","Lane 2"]}]}""";
    private const string DetailQuestions = """{"questions":[{"q":"Kick or swim?","kind":"detail","opts":["Kick","Swim"]},{"q":"Did you skip anything?","kind":"detail","opts":[]}]}""";

    private sealed class Harness
    {
        public readonly InMemoryConversationRepository Conversations = new();
        public readonly InMemoryToolExecutionRepository ToolExecutions;
        public readonly InMemoryDraftRepository Drafts = new();
        public readonly InMemorySessionRepository Sessions = new();
        public readonly ScriptedChatClient Client;
        public readonly ConversationRunner Runner;
        public readonly GatedToolApprovalResolver Resolver;
        public readonly IAiToolRegistry Registry;
        public readonly Conversation Chat;

        public Harness(bool withImage, params ScriptedReply[] script)
        {
            ToolExecutions = new InMemoryToolExecutionRepository(Conversations);
            Client = new ScriptedChatClient(script);
            var unitOfWork = new NoOpUnitOfWork();
            var draftService = new SessionDraftService(Drafts, TestSessions.Ingest(), unitOfWork);

            Registry = new AiToolRegistry(
            [
                new AskUserQuestionTool(), new ReadSwimBoardTool(), new EditDraftTool(draftService),
                new AskSwimQuestionsTool(), new DraftSessionTool(draftService),
                new SaveSessionTool(new SessionPromotionService(draftService, TestSessions.Service(Sessions))),
            ]);

            var agent = new SwimBoardInterviewAgent(draftService, ToolExecutions);
            var profiles = new AgentProfileProvider([agent]);
            var executor = new ToolExecutor(Registry);
            var closer = new ToolTurnCloser(ToolExecutions, Conversations, Registry, unitOfWork, new NoOpCompaction(), executor);
            Resolver = new GatedToolApprovalResolver(closer, ToolExecutions, Conversations, Registry, profiles, [agent]);
            IToolInteraction[] interactions = [new ApprovalInteraction(), new QuestionInteraction(), new SubAgentInteraction(Conversations, [agent])];
            var loop = new ChatTurnLoop(
                Conversations, ToolExecutions, new InMemoryTokenUsageRepository(), unitOfWork, Client, Registry,
                new NoOpCompaction(), executor, closer, profiles, interactions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatTurnLoop>.Instance);
            Runner = new ConversationRunner(loop, Resolver);

            Chat = new Conversation { Id = Guid.NewGuid(), UserId = UserId, Title = "chat", CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow };
            Conversations.Add(Chat);
            Conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(
                Chat.Id, 1, "Here is today's board, I'm in lane 2.", withImage ? [new ImageContentBlock("boards/today.png", "image/png")] : null));
        }

        public Conversation Child => Conversations.Conversations.Single(c => c.Kind == InterviewTools.AgentKind);

        public ToolExecutionRequest ParentRequest => ToolExecutions.Requests.Single(r => r.ToolName == InterviewTools.ReadSwimBoard);

        public ToolExecutionRequest OpenQuestion => ToolExecutions.Requests.Last(r => r.ToolName == InterviewTools.Ask && r.Status == ToolExecutionStatus.PendingApproval);

        public Task<List<AiChatEvent>> RunAsync(Guid conversationId) => Collect(Runner.RunAsync(UserId, conversationId, CancellationToken.None));

        /// <summary>Answers the open question the way the answer endpoint does, then continues into whatever runs next.</summary>
        public async Task<List<AiChatEvent>> AnswerAsync(params string[] answers)
        {
            var events = new List<AiChatEvent>();
            var resolution = new ToolApprovalResolution();
            events.AddRange(await Collect(Resolver.AnswerAsync(UserId, OpenQuestion, answers, resolution, CancellationToken.None)));
            if (resolution.ReadyConversationId is { } ready)
            {
                events.AddRange(await RunAsync(ready));
            }

            return events;
        }

        public async Task<List<AiChatEvent>> CancelAsync()
        {
            var events = new List<AiChatEvent>();
            var resolution = new ToolApprovalResolution();
            events.AddRange(await Collect(Resolver.CancelSubAgentAsync(UserId, ParentRequest, resolution, CancellationToken.None)));
            if (resolution.ReadyConversationId is { } ready)
            {
                events.AddRange(await RunAsync(ready));
            }

            return events;
        }

        public IEnumerable<string> ToolResultsIn(Guid conversationId) => Conversations.Messages
            .Where(m => m.ConversationId == conversationId && m.Role == MessageRole.Tool)
            .SelectMany(m => ChatContentJson.Deserialize(m.RawContentJson).OfType<ToolResultContentBlock>())
            .Select(b => b.ResultJson);
    }

    private static async Task<List<AiChatEvent>> Collect(IAsyncEnumerable<AiChatEvent> events)
    {
        var list = new List<AiChatEvent>();
        await foreach (var chatEvent in events)
        {
            list.Add(chatEvent);
        }

        return list;
    }

    private static ScriptedReply StartBoard() => ScriptedReply.Call(InterviewTools.ReadSwimBoard, """{"context":"Lane 2"}""");

    [Fact]
    public async Task Reading_a_board_starts_an_interview_that_asks_its_first_question_in_the_same_stream()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion));

        var events = await h.RunAsync(h.Chat.Id);

        var started = Assert.Single(events.OfType<AiChatSubAgentStarted>());
        Assert.Equal(h.Chat.Id, started.ParentConversationId);
        Assert.Equal(h.Child.Id, started.ChildConversationId);
        Assert.Equal(h.ParentRequest.Id, started.ToolExecutionRequestId);

        var asked = Assert.Single(events.OfType<AiChatQuestionsAsked>());
        Assert.Equal(h.Child.Id, asked.ConversationId);
        Assert.Equal(h.OpenQuestion.Id, asked.ToolExecutionRequestId);
        var question = Assert.Single(asked.Questions);
        Assert.Equal(AskedQuestionKind.Fork, question.Kind);
        Assert.Equal(["Lane 1", "Lane 2"], question.Options);

        // The main chat is blocked on the running interview, and the client can find the question from the main chat.
        Assert.Equal(ToolExecutionStatus.InProgress, h.ParentRequest.Status);
        Assert.True(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        var pending = await h.ToolExecutions.GetPendingForConversationAsync(UserId, h.Chat.Id, CancellationToken.None);
        Assert.Equal(new[] { h.ParentRequest.Id, h.OpenQuestion.Id }.Order(), pending.Select(p => p.Id).Order());
        Assert.Contains(pending, p => p.Id == h.ParentRequest.Id && p.Status == ToolExecutionStatus.InProgress);
        Assert.Contains(pending, p => p.Id == h.OpenQuestion.Id && p.Status == ToolExecutionStatus.PendingApproval);
        Assert.Equal(h.Child.ParentToolExecutionRequestId, h.ParentRequest.Id);
    }

    [Fact]
    public async Task The_interview_runs_on_its_own_profile_with_the_board_and_what_the_user_already_said()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion));

        await h.RunAsync(h.Chat.Id);

        var outer = h.Client.Requests[0];
        Assert.Contains(outer.Tools, t => t.Name == InterviewTools.ReadSwimBoard);
        Assert.Contains(outer.Tools, t => t.Name == AiToolNames.AskUserQuestion);
        Assert.DoesNotContain(outer.Tools, t => t.Name is InterviewTools.Ask or InterviewTools.DraftSession);
        Assert.Contains(InterviewTools.ReadSwimBoard, outer.SystemPrompt);
        Assert.False(outer.RequireToolUse);

        var interview = h.Client.Requests[1];
        Assert.Equal([InterviewTools.Ask, InterviewTools.DraftSession], interview.Tools.Select(t => t.Name).Order().ToArray());
        Assert.Equal(InterviewPrompt.System, interview.SystemPrompt);
        Assert.True(interview.RequireToolUse);
        Assert.True(interview.PromptCaching);

        var opening = Assert.Single(interview.Messages);
        Assert.Contains(opening.Content, b => b is ImageContentBlock { ObjectKey: "boards/today.png" });
        Assert.Contains(opening.Content, b => b is TextContentBlock t && t.Text.Contains("Board attached") && t.Text.Contains("Lane 2"));
    }

    [Fact]
    public async Task The_board_tool_is_offered_whether_or_not_an_image_is_attached_so_the_cached_tool_list_never_changes()
    {
        var withImage = new Harness(withImage: true, ScriptedReply.Say("Sure."));
        var withoutImage = new Harness(withImage: false, ScriptedReply.Say("Send me a photo of the board."));

        await withImage.RunAsync(withImage.Chat.Id);
        await withoutImage.RunAsync(withoutImage.Chat.Id);

        Assert.Equal(withImage.Client.Requests[0].Tools.Select(t => t.Name), withoutImage.Client.Requests[0].Tools.Select(t => t.Name));
        Assert.Contains(withoutImage.Client.Requests[0].Tools, t => t.Name == InterviewTools.ReadSwimBoard);
    }

    [Fact]
    public async Task Calling_the_board_tool_without_an_image_fails_cleanly_and_the_chat_carries_on()
    {
        var h = new Harness(withImage: false, StartBoard(), ScriptedReply.Say("Send me a photo of the board."));

        await h.RunAsync(h.Chat.Id);

        Assert.Equal(ToolExecutionStatus.Failed, h.ParentRequest.Status);
        Assert.Contains("no image", h.ToolResultsIn(h.Chat.Id).Single(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, h.Client.Requests.Count);
        Assert.DoesNotContain(h.Conversations.Conversations, c => c.Kind == InterviewTools.AgentKind);
    }

    [Fact]
    public async Task A_finished_interview_hands_the_transcript_and_draft_back_and_the_chat_carries_on()
    {
        var h = new Harness(
            withImage: true,
            StartBoard(),
            ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Call(InterviewTools.Ask, DetailQuestions),
            ScriptedReply.Call(InterviewTools.DraftSession, DraftTests.ModelSession),
            ScriptedReply.Say("Here is your draft - does it look right?"));

        await h.RunAsync(h.Chat.Id);
        var afterLane = await h.AnswerAsync("2");
        Assert.Single(afterLane.OfType<AiChatQuestionsAsked>());
        Assert.Equal(2, afterLane.OfType<AiChatQuestionsAsked>().Single().Questions.Count);

        var finish = await h.AnswerAsync("1", "no, all of it");

        var completed = Assert.Single(finish.OfType<AiChatSubAgentCompleted>());
        Assert.True(completed.Success);
        Assert.Equal(h.Chat.Id, completed.ParentConversationId);
        Assert.Contains(finish.OfType<AiChatTextDelta>(), t => t.Text.Contains("does it look right"));
        var last = Assert.IsType<AiChatCompleted>(finish[^1]);
        Assert.Equal(h.Chat.Id, last.ConversationId);

        // The draft exists, is tied to the interview, and the parent tool call now carries what the chat needs.
        var draft = Assert.Single(h.Drafts.Drafts);
        Assert.Equal(h.Child.Id, draft.SourceConversationId);
        Assert.Equal(ToolExecutionStatus.Executed, h.ParentRequest.Status);
        var result = Assert.Single(h.ToolResultsIn(h.Chat.Id));
        Assert.Contains($"Draft id: {draft.Id}", result);
        Assert.Contains("has not been saved", result);
        Assert.Contains("Which lane? -> Lane 2", result);
        Assert.Contains("Kick or swim? -> Kick", result);
        Assert.Contains("Did you skip anything? -> no, all of it", result);
        Assert.Contains("[1.0] 1 x 200 free kick", result);
        Assert.Contains("Distance: 1700", result);

        Assert.False(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        Assert.Equal(0, h.Client.Remaining);
    }

    [Fact]
    public async Task The_interview_ends_the_moment_the_draft_is_accepted_without_another_model_turn()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Call(InterviewTools.DraftSession, DraftTests.ModelSession), ScriptedReply.Say("done"));

        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync("2");

        // 1 outer + 1 ask + 1 draft + 1 outer resume: no extra closing turn in the interview.
        Assert.Equal(4, h.Client.Requests.Count);
        Assert.Equal(h.Chat.Id, h.Conversations.Messages.Last(m => m.Role == MessageRole.Assistant).ConversationId);
    }

    [Fact]
    public async Task An_invalid_draft_is_sent_back_as_an_error_and_the_interviewer_fixes_it_in_the_same_run()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Call(InterviewTools.DraftSession, """{"sets":[{"kind":"item","reps":0,"parts":[{"dist":100,"stroke":"free"}]}]}"""),
            ScriptedReply.Call(InterviewTools.DraftSession, DraftTests.ModelSession),
            ScriptedReply.Say("done"));

        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync("2");

        Assert.Contains(h.ToolResultsIn(h.Child.Id), r => r.Contains("reps must be >= 1"));
        Assert.Single(h.Drafts.Drafts);
        Assert.Equal(ToolExecutionStatus.Executed, h.ParentRequest.Status);
    }

    [Fact]
    public async Task Cancelling_the_interview_unblocks_the_chat_and_tells_the_model_it_was_declined()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Say("No problem - just say when you want to try again."));

        await h.RunAsync(h.Chat.Id);
        var question = h.OpenQuestion;
        var events = await h.CancelAsync();

        Assert.False(Assert.Single(events.OfType<AiChatSubAgentCompleted>()).Success);
        Assert.Equal(ToolExecutionStatus.Denied, h.ParentRequest.Status);
        Assert.Equal(ToolExecutionStatus.Denied, question.Status);
        Assert.Contains("cancelled", Assert.Single(h.ToolResultsIn(h.Chat.Id)));
        Assert.Contains(events.OfType<AiChatTextDelta>(), t => t.Text.Contains("No problem"));
        Assert.False(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        Assert.Empty(h.Drafts.Drafts);
    }

    [Fact]
    public async Task An_interview_that_answers_in_prose_fails_the_tool_call_instead_of_leaving_the_chat_stuck()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Say("I can't read that board."), ScriptedReply.Say("Sorry, I couldn't read it."));

        var events = await h.RunAsync(h.Chat.Id);

        Assert.False(Assert.Single(events.OfType<AiChatSubAgentCompleted>()).Success);
        Assert.Equal(ToolExecutionStatus.Failed, h.ParentRequest.Status);
        Assert.Contains("without producing a result", Assert.Single(h.ToolResultsIn(h.Chat.Id)));
        Assert.False(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_board_call_in_the_same_turn_is_refused_without_stalling_the_first()
    {
        var h = new Harness(withImage: true, new ScriptedReply(
            string.Empty, (InterviewTools.ReadSwimBoard, "{}"), (InterviewTools.ReadSwimBoard, "{}")),
            ScriptedReply.Call(InterviewTools.Ask, LaneQuestion));

        await h.RunAsync(h.Chat.Id);

        var requests = h.ToolExecutions.Requests.Where(r => r.ToolName == InterviewTools.ReadSwimBoard).ToList();
        Assert.Equal(ToolExecutionStatus.InProgress, requests[0].Status);
        Assert.Equal(ToolExecutionStatus.Failed, requests[1].Status);
        Assert.Single(h.Conversations.Conversations, c => c.Kind == InterviewTools.AgentKind);
    }

    [Fact]
    public async Task The_main_chat_can_edit_the_draft_afterwards_by_path()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Call(InterviewTools.DraftSession, DraftTests.ModelSession), ScriptedReply.Say("done"));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync("2");
        var draft = Assert.Single(h.Drafts.Drafts);

        var edit = (IAiTool)h.Registry.Find(InterviewTools.EditDraft)!;
        var result = await edit.ExecuteAsync(
            new ToolExecutionContext(UserId, h.Chat.Id, "toolu_x"),
            $$"""{"draftId":"{{draft.Id}}","edits":[{"op":"remove","path":"1.0"}]}""",
            CancellationToken.None);

        Assert.Contains("version 2", result.ResultJson);
        Assert.Contains("[1] 3 rounds", result.ResultJson);
        Assert.DoesNotContain("1 x 200 free kick", result.ResultJson);
        Assert.Equal(2, draft.Version);
    }

    [Fact]
    public async Task Saving_the_draft_is_gated_and_then_stores_a_session_the_chat_can_refer_to()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Call(InterviewTools.Ask, LaneQuestion),
            ScriptedReply.Call(InterviewTools.DraftSession, DraftTests.ModelSession), ScriptedReply.Say("done"));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync("2");
        var draft = Assert.Single(h.Drafts.Drafts);

        var save = (IAiTool)h.Registry.Find(InterviewTools.SaveSession)!;
        Assert.True(save.RequiresApproval);
        Assert.Equal(ToolInteractionKind.Approval, save.Interaction);
        Assert.Empty(h.Sessions.Sessions);

        var input = $$"""{"draftId":"{{draft.Id}}","performedOn":"2026-09-21","course":"scy","title":"Monday lane 2"}""";
        var result = await save.ExecuteAsync(new ToolExecutionContext(UserId, h.Chat.Id, "toolu_save"), input, CancellationToken.None);
        var again = await save.ExecuteAsync(new ToolExecutionContext(UserId, h.Chat.Id, "toolu_save2"), input, CancellationToken.None);

        var session = Assert.Single(h.Sessions.Sessions);
        Assert.Equal(SessionSource.Ai, session.Source);
        Assert.Equal(PoolCourse.Scy, session.Course);
        Assert.Equal(new DateOnly(2026, 9, 21), session.PerformedOn);
        Assert.Equal(1700, session.TotalDistance);
        Assert.Contains(session.Id.ToString(), result.ResultJson);
        Assert.Equal(result.ResultJson, again.ResultJson);
        Assert.Equal(1700, h.Sessions.Facts.Sum(f => f.Distance));
    }

    [Theory]
    [InlineData("""{"draftId":"nope","performedOn":"2026-09-21","course":"scy"}""")]
    [InlineData("""{"draftId":"00000000-0000-0000-0000-000000000000","performedOn":"2026-09-21","course":"olympic"}""")]
    public async Task Saving_with_a_bad_draft_id_or_course_is_an_error_the_model_can_correct(string input)
    {
        var h = new Harness(withImage: false);
        var save = (IAiTool)h.Registry.Find(InterviewTools.SaveSession)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            save.ExecuteAsync(new ToolExecutionContext(UserId, h.Chat.Id, "toolu_x"), input, CancellationToken.None));
        Assert.Empty(h.Sessions.Sessions);
    }
}
