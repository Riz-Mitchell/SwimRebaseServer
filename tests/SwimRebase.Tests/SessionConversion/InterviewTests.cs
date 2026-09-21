using SessionConversion.Application.Interview;

namespace SwimRebase.Tests.SessionConversion;

public class InterviewTests
{
    private const string Batch = """
        {"questions":[
          {"q":"Which lane?","kind":"fork","opts":["Lane 1","Lane 2","Lane 3"]},
          {"q":"Kick or swim?","kind":"detail","opts":["Kick","Swim"]}]}
        """;

    private const string DetailsThenFork = """
        {"questions":[
          {"q":"Stroke?","kind":"detail","opts":["Fly","Back"]},
          {"q":"Fins?","kind":"detail","opts":[]},
          {"q":"Sprint or distance?","kind":"fork","opts":["Sprint","Distance"]}]}
        """;

    [Fact]
    public void Questions_after_a_fork_are_held_back()
    {
        var (asked, held) = InterviewQuestions.SplitAtFork(InterviewQuestions.Parse(Batch));

        Assert.Single(asked);
        Assert.True(asked[0].IsFork);
        Assert.Equal(1, held);
    }

    [Fact]
    public void Details_before_a_fork_are_batched_with_it()
    {
        var asked = InterviewQuestions.Asked(DetailsThenFork);

        Assert.Equal(3, asked.Count);
        Assert.True(asked[^1].IsFork);
    }

    [Fact]
    public void Answers_are_numbered_and_option_numbers_become_the_option_text()
    {
        var result = InterviewQuestions.FormatAnswer(DetailsThenFork, ["2", "no fins", "1"], lastChance: false);

        Assert.Equal("1. Back\n2. no fins\n3. Sprint".Replace("\n", Environment.NewLine), result);
    }

    [Fact]
    public void A_missing_or_blank_answer_is_marked_skipped_and_typed_text_passes_through()
    {
        var result = InterviewQuestions.FormatAnswer(DetailsThenFork, ["Butterfly, mostly", "  "], lastChance: false);

        Assert.Contains("1. Butterfly, mostly", result);
        Assert.Contains("2. (skipped)", result);
        Assert.Contains("3. (skipped)", result);
    }

    [Fact]
    public void An_answer_spanning_lines_stays_on_one_line()
    {
        var result = InterviewQuestions.FormatAnswer(DetailsThenFork, ["did\nfly\r\nonly"], lastChance: false);

        Assert.Contains("1. did fly only", result);
    }

    [Fact]
    public void Held_back_questions_and_the_turn_limit_are_reported_to_the_model()
    {
        var result = InterviewQuestions.FormatAnswer(Batch, ["2"], lastChance: true);

        Assert.Contains("1. Lane 2", result);
        Assert.Contains("1 later question(s) were not asked", result);
        Assert.Contains("call draft_session now", result);
    }

    [Fact]
    public void An_ask_with_no_questions_gets_a_nudge_instead_of_silence()
    {
        Assert.Contains("No questions received", InterviewQuestions.FormatAnswer("""{"questions":[]}""", [], lastChance: false));
    }

    [Fact]
    public void The_transcript_pairs_each_asked_question_with_its_answer()
    {
        var answers = InterviewQuestions.FormatAnswer(Batch, ["Lane 2"], lastChance: false);

        var transcript = InterviewTranscript.Build([(Batch, answers)]);

        Assert.Equal("- Which lane? -> Lane 2", transcript);
    }
}
