using SessionConversion.Application.Interview;

namespace SwimRebase.Tests.SessionConversion;

public class InterviewTests
{
    private const string Model = """
        {"questions":[
          {"id":"q1","q":"Which lane?","kind":"fork","opts":["Lane 1","Lane 2"],"showIf":null},
          {"id":"q2","q":"Kick or swim?","kind":"detail","opts":["Kick","Swim"],"showIf":{"qid":"q1","opt":"Lane 1"}},
          {"id":"q3","q":"Fins or not?","kind":"detail","opts":["Fins","No fins"],"showIf":{"qid":"q1","opt":"Lane 2"}},
          {"id":"q4","q":"Stroke for the main set?","kind":"detail","opts":["Fly","Back"],"showIf":null}]}
        """;

    private static readonly string Questionnaire = InterviewQuestions.Compose(Model);

    [Fact]
    public void The_app_adds_the_change_date_and_pool_questions_after_the_models()
    {
        var questions = InterviewQuestions.Parse(Questionnaire);

        Assert.Equal(["q1", "q2", "q3", "q4", InterviewFields.ChangedId, InterviewFields.DateId, InterviewFields.CourseId], questions.Select(q => q.Id));
        Assert.Equal(InterviewQuestionKind.Field, questions[^2].Kind);
        Assert.Equal(InterviewFields.DateType, questions[^2].FieldType);
        Assert.Equal(InterviewFields.Courses, questions[^1].Options);
    }

    [Fact]
    public void A_condition_on_an_unknown_or_later_question_is_dropped_so_the_question_is_asked()
    {
        var model = """{"questions":[{"id":"a","q":"First?","kind":"detail","opts":[],"showIf":{"qid":"b","opt":"x"}},{"id":"b","q":"Second?","kind":"fork","opts":["x"],"showIf":null}]}""";

        var questions = InterviewQuestions.Parse(InterviewQuestions.Compose(model));

        Assert.Null(questions[0].ShowIf);
    }

    [Fact]
    public void Answers_are_numbered_by_position_and_questions_on_the_branch_not_taken_are_left_out()
    {
        var result = InterviewQuestions.FormatAnswer(Questionnaire, ["2", "", "1", "2", "1", "2026-09-30", "1"]);

        Assert.Equal(
            "1. Lane 2\n3. Fins\n4. Back\n5. Everything as written\n6. 2026-09-30\n7. scy".Replace("\n", Environment.NewLine),
            result);
    }

    [Fact]
    public void A_branch_question_under_a_hidden_fork_is_hidden_too()
    {
        var model = """{"questions":[{"id":"a","q":"A?","kind":"fork","opts":["x","y"],"showIf":null},{"id":"b","q":"B?","kind":"fork","opts":["p"],"showIf":{"qid":"a","opt":"x"}},{"id":"c","q":"C?","kind":"detail","opts":[],"showIf":{"qid":"b","opt":"p"}}]}""";
        var questions = InterviewQuestions.Parse(InterviewQuestions.Compose(model));

        var visible = InterviewQuestions.VisibleAnswers(questions, ["y", "p", "anything"]);

        Assert.Equal(["a", InterviewFields.ChangedId, InterviewFields.DateId, InterviewFields.CourseId], visible.Select(v => v.Question.Id));
    }

    [Fact]
    public void A_missing_or_blank_answer_is_marked_skipped_and_typed_text_passes_through()
    {
        var result = InterviewQuestions.FormatAnswer(Questionnaire, ["Lane 1", "Butterfly, mostly", "  "]);

        Assert.Contains("1. Lane 1", result);
        Assert.Contains("2. Butterfly, mostly", result);
        Assert.Contains("4. (skipped)", result);
    }

    [Fact]
    public void An_answer_spanning_lines_stays_on_one_line()
    {
        var result = InterviewQuestions.FormatAnswer(Questionnaire, ["Lane 1", "did\nfly\r\nonly"]);

        Assert.Contains("2. did fly only", result);
    }

    [Fact]
    public void The_transcript_pairs_each_question_that_applied_with_its_answer_and_leaves_out_the_app_fields()
    {
        var answers = InterviewQuestions.FormatAnswer(Questionnaire, ["Lane 2", "", "No fins", "Back", "I skipped the last set", "2026-09-30", "lcm"]);

        var transcript = InterviewTranscript.Build([(Questionnaire, answers)]);

        Assert.Equal(
            "- Which lane? -> Lane 2\n- Fins or not? -> No fins\n- Stroke for the main set? -> Back\n- Did you do everything as written, or skip, shorten, change or add anything? -> I skipped the last set"
                .Replace("\n", Environment.NewLine),
            transcript);
    }

    [Fact]
    public void A_questionnaire_nobody_has_answered_yet_reads_as_no_answers()
    {
        Assert.Contains("- Which lane? -> (no answer)", InterviewTranscript.Build([(Questionnaire, null)]));
    }

    [Theory]
    [InlineData("2026-09-30", "2", "2026-09-30", "scm")]
    [InlineData("30 Sep 2026", "LCM", "2026-09-30", "lcm")]
    [InlineData("yesterday", "scy", null, "scy")]
    [InlineData("", "olympic", null, null)]
    public void The_date_and_pool_are_read_from_the_answers_when_they_are_valid(string date, string course, string? expectedDate, string? expectedCourse)
    {
        var answers = InterviewQuestions.FormatAnswer(Questionnaire, ["Lane 1", "Kick", "", "Fly", "Everything as written", date, course]);

        var swamOn = InterviewTranscript.SwamOn(Questionnaire, answers);

        Assert.Equal(expectedDate, swamOn?.ToString("yyyy-MM-dd"));
        Assert.Equal(expectedCourse, InterviewTranscript.Course(Questionnaire, answers));
    }
}
