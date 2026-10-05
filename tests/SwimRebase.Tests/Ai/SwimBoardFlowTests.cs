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
/// Drives the real turn loop, conversion driver, runner, resolver and interactions - with in-memory storage and a scripted model - through a
/// swim-board conversion nested inside a chat, the way the orchestrator's endpoints would.
/// </summary>
public class SwimBoardFlowTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private const string Questionnaire = """
        {"questions":[
          {"id":"q1","q":"Which lane?","kind":"fork","opts":["Lane 1","Lane 2"],"showIf":null},
          {"id":"q2","q":"Kick or swim?","kind":"detail","opts":["Kick","Swim"],"showIf":{"qid":"q1","opt":"Lane 2"}}]}
        """;

    private const string InvalidSession = """{"ledger":[],"group":null,"sets":[{"kind":"item","reps":0,"timing":null,"parts":[{"dist":100,"stroke":"free","mode":"swim","equip":[],"effort":null}],"note":null}]}""";

    /// <summary>Lane 2, kick, everything as written, 21 Sep 2026, short course yards - what the questionnaire's five questions are answered with.</summary>
    private static readonly string[] FullAnswers = ["2", "Kick", "1", "2026-09-21", "1"];

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
            var sessionService = TestSessions.Service(Sessions);

            Registry = new AiToolRegistry(
            [
                new AskUserQuestionTool(), new ReadSwimBoardTool(), new EditDraftTool(draftService),
                new AskSwimQuestionsTool(),
                new SaveSessionTool(new SessionPromotionService(draftService, sessionService), draftService),
            ]);

            var agent = new SwimBoardInterviewAgent(draftService, ToolExecutions);
            var profiles = new AgentProfileProvider([agent]);
            var executor = new ToolExecutor(Registry);
            var closer = new ToolTurnCloser(ToolExecutions, Conversations, Registry, unitOfWork, new NoOpCompaction(), executor);
            Resolver = new GatedToolApprovalResolver(closer, ToolExecutions, Conversations, Registry, profiles, [agent]);
            IToolInteraction[] interactions = [new ApprovalInteraction(), new QuestionInteraction(), new SubAgentInteraction(Conversations, [agent])];
            var tokenUsage = new InMemoryTokenUsageRepository();
            var loop = new ChatTurnLoop(
                Conversations, ToolExecutions, tokenUsage, unitOfWork, Client, Registry,
                new NoOpCompaction(), executor, closer, profiles, interactions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ChatTurnLoop>.Instance);
            var driver = new SwimBoardConversionDriver(
                Conversations, ToolExecutions, tokenUsage, unitOfWork, Client, Registry, draftService, sessionService,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SwimBoardConversionDriver>.Instance);
            var followUp = new SubAgentFollowUp(ToolExecutions, Conversations, Registry, [agent], unitOfWork);
            Runner = new ConversationRunner(loop, Resolver, followUp, Conversations, [driver]);

            Chat = new Conversation { Id = Guid.NewGuid(), UserId = UserId, Title = "chat", CreatedAt = DateTimeOffset.UtcNow, LastMessageAt = DateTimeOffset.UtcNow };
            Conversations.Add(Chat);
            Conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(
                Chat.Id, 1, "Here is today's board, I'm in lane 2.", withImage ? [new ImageContentBlock("boards/today.png", "image/png")] : null));
        }

        public Conversation Child => Conversations.Conversations.Single(c => c.Kind == InterviewTools.AgentKind);

        public ToolExecutionRequest ParentRequest => ToolExecutions.Requests.Single(r => r.ToolName == InterviewTools.ReadSwimBoard);

        public ToolExecutionRequest OpenQuestion => ToolExecutions.Requests.Last(r => r.ToolName == InterviewTools.Ask && r.Status == ToolExecutionStatus.PendingApproval);

        public ToolExecutionRequest SaveRequest => ToolExecutions.Requests.Single(r => r.ToolName == InterviewTools.SaveSession);

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

        /// <summary>Approves or declines the save card the way the resolve endpoint does, then continues into the main chat's next turn.</summary>
        public async Task<List<AiChatEvent>> ResolveSaveAsync(bool approve)
        {
            var events = new List<AiChatEvent>();
            var resolution = new ToolApprovalResolution();
            events.AddRange(await Collect(Resolver.ResolveAsync(UserId, SaveRequest, approve, approve ? null : "not quite", resolution, CancellationToken.None)));
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
    public async Task Reading_a_board_asks_every_question_at_once_in_the_same_stream()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire));

        var events = await h.RunAsync(h.Chat.Id);

        var started = Assert.Single(events.OfType<AiChatSubAgentStarted>());
        Assert.Equal(h.Chat.Id, started.ParentConversationId);
        Assert.Equal(h.Child.Id, started.ChildConversationId);
        Assert.Equal(h.ParentRequest.Id, started.ToolExecutionRequestId);

        var asked = Assert.Single(events.OfType<AiChatQuestionsAsked>());
        Assert.Equal(h.Child.Id, asked.ConversationId);
        Assert.Equal(h.OpenQuestion.Id, asked.ToolExecutionRequestId);
        Assert.Equal(
            [AskedQuestionKind.Fork, AskedQuestionKind.Detail, AskedQuestionKind.Detail, AskedQuestionKind.Field, AskedQuestionKind.Field],
            asked.Questions.Select(q => q.Kind));
        Assert.Equal(["q1", "q2", InterviewFields.ChangedId, InterviewFields.DateId, InterviewFields.CourseId], asked.Questions.Select(q => q.Id));
        Assert.Equal(new AskedQuestionCondition("q1", "Lane 2"), asked.Questions[1].ShowIf);
        Assert.Equal(InterviewFields.DateType, asked.Questions[3].FieldType);
        Assert.Equal(["scy", "scm", "lcm"], asked.Questions[4].Options);

        // The main chat is blocked on the running conversion, and the client can find the questions from the main chat.
        Assert.Equal(ToolExecutionStatus.InProgress, h.ParentRequest.Status);
        Assert.True(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        var pending = await h.ToolExecutions.GetPendingForConversationAsync(UserId, h.Chat.Id, CancellationToken.None);
        Assert.Equal(new[] { h.ParentRequest.Id, h.OpenQuestion.Id }.Order(), pending.Select(p => p.Id).Order());
        Assert.Equal(h.Child.ParentToolExecutionRequestId, h.ParentRequest.Id);
    }

    [Fact]
    public async Task Each_call_is_answered_as_json_of_its_own_schema_with_the_board_and_what_the_user_already_said()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession));

        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        var outer = h.Client.Requests[0];
        Assert.Contains(outer.Tools, t => t.Name == InterviewTools.ReadSwimBoard);
        Assert.DoesNotContain(outer.Tools, t => t.Name == InterviewTools.Ask);
        Assert.Contains(InterviewTools.ReadSwimBoard, outer.SystemPrompt);
        Assert.Null(outer.OutputSchema);

        var questionnaire = h.Client.Requests[1];
        Assert.Empty(questionnaire.Tools);
        Assert.Equal(InterviewPrompt.Questionnaire, questionnaire.SystemPrompt);
        Assert.Equal(ConversionSchemas.Questionnaire, questionnaire.OutputSchema);
        var opening = Assert.Single(questionnaire.Messages);
        Assert.Contains(opening.Content, b => b is ImageContentBlock { ObjectKey: "boards/today.png" });
        Assert.Contains(opening.Content, b => b is TextContentBlock t && t.Text.Contains("Board attached") && t.Text.Contains("Lane 2"));

        var conversion = h.Client.Requests[2];
        Assert.Empty(conversion.Tools);
        Assert.Equal(InterviewPrompt.Conversion, conversion.SystemPrompt);
        Assert.Equal(ConversionSchemas.Session, conversion.OutputSchema);
        var board = Assert.Single(conversion.Messages);
        Assert.Contains(board.Content, b => b is ImageContentBlock { ObjectKey: "boards/today.png" });
        Assert.Contains(board.Content, b => b is TextContentBlock t && t.Text.Contains("Which lane? -> Lane 2") && t.Text.Contains("Kick or swim? -> Kick"));
    }

    [Fact]
    public async Task A_questionnaire_that_cannot_be_read_fails_the_tool_call_instead_of_leaving_the_chat_stuck()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say("I can't read that board."), ScriptedReply.Say("Sorry, I couldn't read it."));

        var events = await h.RunAsync(h.Chat.Id);

        Assert.False(Assert.Single(events.OfType<AiChatSubAgentCompleted>()).Success);
        Assert.Equal(ToolExecutionStatus.Failed, h.ParentRequest.Status);
        Assert.Contains("without producing a result", Assert.Single(h.ToolResultsIn(h.Chat.Id)));
        Assert.Contains(events.OfType<AiChatTextDelta>(), t => t.Text.Contains("Sorry"));
        Assert.False(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        Assert.Empty(h.Drafts.Drafts);
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
    public async Task Answering_converts_the_board_in_one_call_and_puts_the_draft_to_the_user_to_save_without_a_chat_turn()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession), ScriptedReply.Say("Saved."));

        await h.RunAsync(h.Chat.Id);
        var finish = await h.AnswerAsync(FullAnswers);

        var completed = Assert.Single(finish.OfType<AiChatSubAgentCompleted>());
        Assert.True(completed.Success);
        Assert.Equal(h.Chat.Id, completed.ParentConversationId);

        // The draft exists, is tied to the conversion, and the parent tool call now carries what the chat needs.
        var draft = Assert.Single(h.Drafts.Drafts);
        Assert.Equal(h.Child.Id, draft.SourceConversationId);
        Assert.Equal(ToolExecutionStatus.Executed, h.ParentRequest.Status);
        var result = Assert.Single(h.ToolResultsIn(h.Chat.Id));
        Assert.Contains($"Draft id: {draft.Id}", result);
        Assert.Contains("has not been saved", result);
        Assert.Contains("Save button", result);
        Assert.Contains("swam on 2026-09-21 in a scy pool", result);
        Assert.Contains("Which lane? -> Lane 2", result);
        Assert.Contains("Kick or swim? -> Kick", result);
        Assert.Contains("Did you do everything as written, or skip, shorten, change or add anything? -> Everything as written", result);
        Assert.Contains("[1.0] 1 x 200 free kick", result);
        Assert.Contains("Distance: 1700", result);

        // The save card is raised by the server straight after the conversion: the draft attached, date and pool filled in, no main-chat model turn.
        var card = Assert.Single(finish.OfType<AiChatPermissionRequired>());
        Assert.Equal(h.Chat.Id, card.ConversationId);
        Assert.Equal(InterviewTools.SaveSession, card.ToolName);
        Assert.Equal(h.SaveRequest.Id, card.ToolExecutionRequestId);
        Assert.Contains($"\"draftId\":\"{draft.Id}\"", card.InputJson);
        Assert.Contains("\"performedOn\":\"2026-09-21\"", card.InputJson);
        Assert.Contains("\"course\":\"scy\"", card.InputJson);
        Assert.Contains("Distance: 1700", card.Preview);
        Assert.Contains(finish.OfType<AiChatTextDelta>(), t => t.Text.Contains("2026-09-21") && t.Text.Contains("scy"));
        Assert.Equal(ToolExecutionStatus.PendingApproval, h.SaveRequest.Status);
        Assert.IsType<AiChatCompleted>(finish[^1]);
        Assert.Equal(h.Chat.Id, ((AiChatCompleted)finish[^1]).ConversationId);
        Assert.True(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));

        // outer chat + questionnaire + conversion: nothing else ran before the user saw the card.
        Assert.Equal(3, h.Client.Requests.Count);
        Assert.Empty(h.Sessions.Sessions);
    }

    [Fact]
    public async Task Accepting_the_card_saves_the_session_and_the_chat_confirms()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession), ScriptedReply.Say("Saved."));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        var events = await h.ResolveSaveAsync(approve: true);

        var session = Assert.Single(h.Sessions.Sessions);
        Assert.Equal(new DateOnly(2026, 9, 21), session.PerformedOn);
        Assert.Equal(PoolCourse.Scy, session.Course);
        Assert.Equal(1700, session.TotalDistance);
        Assert.Contains(events.OfType<AiChatTextDelta>(), t => t.Text.Contains("Saved."));
        Assert.False(await h.ToolExecutions.HasOutstandingRequestAsync(UserId, h.Chat.Id, CancellationToken.None));
        Assert.Equal(0, h.Client.Remaining);

        var saveResult = h.ToolResultsIn(h.Chat.Id).Last();
        Assert.StartsWith("The user reviewed this and approved it.", saveResult);
        Assert.Contains(session.Id.ToString(), saveResult);
    }

    [Fact]
    public async Task Declining_the_card_hands_the_reason_back_to_the_chat_and_saves_nothing()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession), ScriptedReply.Say("What should I change?"));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        var events = await h.ResolveSaveAsync(approve: false);

        Assert.Empty(h.Sessions.Sessions);
        Assert.Contains("not quite", h.ToolResultsIn(h.Chat.Id).Last());
        Assert.Contains(events.OfType<AiChatTextDelta>(), t => t.Text.Contains("What should I change?"));
    }

    [Fact]
    public async Task Without_a_valid_date_and_pool_the_chat_resumes_instead_of_offering_a_save()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession), ScriptedReply.Say("Which date and pool was that?"));
        await h.RunAsync(h.Chat.Id);

        var finish = await h.AnswerAsync("2", "Kick", "1", "", "1");

        Assert.Empty(finish.OfType<AiChatPermissionRequired>());
        Assert.DoesNotContain(h.ToolExecutions.Requests, r => r.ToolName == InterviewTools.SaveSession);
        var result = Assert.Single(h.ToolResultsIn(h.Chat.Id));
        Assert.DoesNotContain("Save button", result);
        Assert.DoesNotContain("swam on", result);
        Assert.Contains(finish.OfType<AiChatTextDelta>(), t => t.Text.Contains("Which date and pool"));
        Assert.Single(h.Drafts.Drafts);
    }

    [Fact]
    public async Task An_invalid_draft_is_repaired_once_with_the_exact_problems()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(InvalidSession), ScriptedReply.Say(DraftTests.ModelSession));
        await h.RunAsync(h.Chat.Id);

        var finish = await h.AnswerAsync(FullAnswers);

        var repair = h.Client.Requests[3];
        Assert.Contains(repair.Messages.Last().Content, b => b is TextContentBlock t && t.Text.Contains("reps must be >= 1"));
        Assert.Single(h.Drafts.Drafts);
        Assert.Equal(ToolExecutionStatus.Executed, h.ParentRequest.Status);
        Assert.Single(finish.OfType<AiChatPermissionRequired>());
    }

    [Fact]
    public async Task A_draft_that_stays_invalid_ends_the_conversion_without_a_draft_and_the_chat_is_told()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(InvalidSession), ScriptedReply.Say(InvalidSession),
            ScriptedReply.Say("Sorry, I couldn't turn that board into a session."));
        await h.RunAsync(h.Chat.Id);

        var finish = await h.AnswerAsync(FullAnswers);

        Assert.Empty(h.Drafts.Drafts);
        Assert.Equal(ToolExecutionStatus.Failed, h.ParentRequest.Status);
        Assert.Contains("without producing a result", Assert.Single(h.ToolResultsIn(h.Chat.Id)));
        Assert.Empty(finish.OfType<AiChatPermissionRequired>());
        Assert.Contains(finish.OfType<AiChatTextDelta>(), t => t.Text.Contains("Sorry"));
    }

    [Fact]
    public async Task Cancelling_the_conversion_unblocks_the_chat_and_tells_the_model_it_was_declined()
    {
        var h = new Harness(
            withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire),
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
    public async Task A_second_board_call_in_the_same_turn_is_refused_without_stalling_the_first()
    {
        var h = new Harness(withImage: true, new ScriptedReply(
            string.Empty, (InterviewTools.ReadSwimBoard, "{}"), (InterviewTools.ReadSwimBoard, "{}")),
            ScriptedReply.Say(Questionnaire));

        await h.RunAsync(h.Chat.Id);

        var requests = h.ToolExecutions.Requests.Where(r => r.ToolName == InterviewTools.ReadSwimBoard).ToList();
        Assert.Equal(ToolExecutionStatus.InProgress, requests[0].Status);
        Assert.Equal(ToolExecutionStatus.Failed, requests[1].Status);
        Assert.Single(h.Conversations.Conversations, c => c.Kind == InterviewTools.AgentKind);
    }

    [Fact]
    public async Task The_main_chat_can_edit_the_draft_afterwards_by_path()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);
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
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);
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

    [Fact]
    public async Task The_save_preview_shows_the_draft_and_nothing_for_an_unknown_one()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);
        var draft = Assert.Single(h.Drafts.Drafts);
        var save = (IToolPreview)h.Registry.Find(InterviewTools.SaveSession)!;

        var preview = await save.PreviewAsync(UserId, $$"""{"draftId":"{{draft.Id}}","performedOn":"2026-09-21","course":"scy"}""", CancellationToken.None);

        Assert.Contains("Distance: 1700", preview);
        Assert.Null(await save.PreviewAsync(UserId, $$"""{"draftId":"{{Guid.NewGuid()}}"}""", CancellationToken.None));
        Assert.Null(await save.PreviewAsync(UserId, "not json", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"draftId":"nope","performedOn":"2026-09-21","course":"scy"}""")]
    [InlineData("""{"draftId":"00000000-0000-0000-0000-000000000000","performedOn":"2026-09-21","course":"olympic"}""")]
    [InlineData("""{"draftId":"00000000-0000-0000-0000-000000000000","course":"scy"}""")]
    [InlineData("""{"draftId":"00000000-0000-0000-0000-000000000000","performedOn":"0001-01-01","course":"scy"}""")]
    public async Task Saving_with_a_bad_draft_id_date_or_course_is_an_error_the_model_can_correct(string input)
    {
        var h = new Harness(withImage: false);
        var save = (IAiTool)h.Registry.Find(InterviewTools.SaveSession)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            save.ExecuteAsync(new ToolExecutionContext(UserId, h.Chat.Id, "toolu_x"), input, CancellationToken.None));
        Assert.Empty(h.Sessions.Sessions);
    }

    private static readonly string TitledSession = DraftTests.ModelSession.Replace("{\"group\":\"Lane 2\",", "{\"group\":\"Lane 2\",\"title\":\"Fly kick ladder\",");

    [Fact]
    public async Task The_conversion_is_asked_for_a_title_and_the_schema_has_a_place_for_it()
    {
        Assert.Contains("\"title\"", ConversionSchemas.Session);
        Assert.Contains("`title`", InterviewPrompt.Conversion);
        Assert.Contains("Never the date", InterviewPrompt.Conversion);

        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(TitledSession));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        Assert.Equal("Fly kick ladder", Assert.IsType<SwimSession>(SessionJson.Deserialize<SwimSession>(Assert.Single(h.Drafts.Drafts).ContentJson)).Title);
    }

    [Fact]
    public async Task The_save_card_carries_the_drafts_title_and_saving_uses_it()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(TitledSession), ScriptedReply.Say("Saved."));
        await h.RunAsync(h.Chat.Id);
        var finish = await h.AnswerAsync(FullAnswers);

        var card = Assert.Single(finish.OfType<AiChatPermissionRequired>());
        Assert.Contains("\"title\":\"Fly kick ladder\"", card.InputJson);

        await h.ResolveSaveAsync(approve: true);

        var session = Assert.Single(h.Sessions.Sessions);
        Assert.Equal("Fly kick ladder", session.Title);
        Assert.Null(SessionJson.Deserialize<SwimSession>(session.ContentJson).Title);
    }

    [Fact]
    public async Task A_board_the_model_gave_no_title_for_is_named_from_what_was_swum()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(DraftTests.ModelSession), ScriptedReply.Say("Saved."));
        await h.RunAsync(h.Chat.Id);
        var finish = await h.AnswerAsync(FullAnswers);

        Assert.Contains("\"title\":null", Assert.Single(finish.OfType<AiChatPermissionRequired>()).InputJson);

        await h.ResolveSaveAsync(approve: true);

        Assert.Equal("Lane 2 - Freestyle and kick - 1,700 yd", Assert.Single(h.Sessions.Sessions).Title);
    }

    [Fact]
    public async Task The_conversion_is_shown_the_swimmers_recent_titles_so_it_can_choose_a_different_one()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(TitledSession));
        var now = DateTimeOffset.UtcNow;
        h.Sessions.Add(new Session { Id = Guid.NewGuid(), OwnerId = UserId, Title = "Sprint starts", PerformedOn = new DateOnly(2026, 9, 14), CreatedAt = now }, []);
        h.Sessions.Add(new Session { Id = Guid.NewGuid(), OwnerId = UserId, Title = "Threshold 200s", PerformedOn = new DateOnly(2026, 9, 18), CreatedAt = now }, []);
        h.Sessions.Add(new Session { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Title = "Someone else's", PerformedOn = new DateOnly(2026, 9, 19), CreatedAt = now }, []);

        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        var conversion = h.Client.Requests[2];
        var text = string.Join('\n', conversion.Messages.SelectMany(m => m.Content).OfType<TextContentBlock>().Select(t => t.Text));
        Assert.Contains("recent session titles", text);
        Assert.Contains("\"Threshold 200s\"; \"Sprint starts\"", text);
        Assert.DoesNotContain("Someone else's", text);
        Assert.DoesNotContain("recent session titles", conversion.SystemPrompt);
    }

    [Fact]
    public async Task A_first_conversion_with_no_saved_sessions_mentions_no_titles()
    {
        var h = new Harness(withImage: true, StartBoard(), ScriptedReply.Say(Questionnaire), ScriptedReply.Say(TitledSession));
        await h.RunAsync(h.Chat.Id);
        await h.AnswerAsync(FullAnswers);

        var text = string.Join('\n', h.Client.Requests[2].Messages.SelectMany(m => m.Content).OfType<TextContentBlock>().Select(t => t.Text));
        Assert.DoesNotContain("recent session titles", text);
    }
}
