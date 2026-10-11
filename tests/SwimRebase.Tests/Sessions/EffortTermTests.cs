using SessionConversion.Application.Interview;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class EffortTermTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void A_swimmers_own_term_matches_the_whole_text_ignoring_case_and_spacing()
    {
        var mapper = new PersonalIntensityMapper([new UserEffortTerm { Phrase = "wolf pace", Percent = 85 }]);

        Assert.True(mapper.TryMap("  Wolf   PACE ", out var intensity));
        Assert.Equal(85, intensity.Percent);
        Assert.Equal(PersonalIntensityMapper.PersonalScale, intensity.Scale);
        Assert.False(mapper.TryMap("wolf pace x4", out _));
    }

    [Fact]
    public void A_personal_term_wins_over_the_built_in_vocabulary()
    {
        var resolver = TestSessions.Resolver();
        var personal = new PersonalIntensityMapper([new UserEffortTerm { Phrase = "threshold", Percent = 95 }]);

        Assert.Equal(95, resolver.Resolve("Threshold", personal)?.Percent);
        Assert.Equal(83, resolver.Resolve("Threshold")?.Percent);
    }

    [Fact]
    public async Task Only_wording_nothing_understands_is_unknown_and_each_is_listed_once()
    {
        var terms = TestSessions.Terms();

        var unknown = await terms.UnknownAsync(
            UserId, ["Wolf pace", "easy", "A2", "wolf  PACE", "80%", "", new string('x', 41), "Zone 4"], CancellationToken.None);

        Assert.Equal(["Wolf pace", "Zone 4"], unknown);
    }

    [Fact]
    public async Task A_saved_meaning_stops_the_wording_being_unknown_and_applies_only_to_that_swimmer()
    {
        var terms = TestSessions.Terms();

        await terms.SaveAsync(UserId, [("Wolf pace", "A2")], CancellationToken.None);

        Assert.Empty(await terms.UnknownAsync(UserId, ["wolf pace"], CancellationToken.None));
        Assert.Equal(["wolf pace"], await terms.UnknownAsync(Guid.NewGuid(), ["wolf pace"], CancellationToken.None));
    }

    [Fact]
    public async Task Meanings_read_as_a_zone_a_word_or_a_percent_and_one_that_is_not_an_effort_is_ignored()
    {
        var repository = new InMemoryUserEffortTermRepository();
        var terms = TestSessions.Terms(repository);

        await terms.SaveAsync(UserId, [("Wolf pace", "A2"), ("Zone 4", "76%"), ("Cruise", "sub max"), ("Odd", "no idea")], CancellationToken.None);

        Assert.Equal(
            [("wolf pace", 70), ("zone 4", 76), ("cruise", 90)],
            repository.Terms.Select(t => (t.Phrase, t.Percent)));
    }

    [Fact]
    public async Task Saving_a_phrase_again_replaces_its_percent()
    {
        var repository = new InMemoryUserEffortTermRepository();
        var terms = TestSessions.Terms(repository);

        await terms.SaveAsync(UserId, [("Wolf pace", "70%")], CancellationToken.None);
        await terms.SaveAsync(UserId, [("wolf PACE", "85%")], CancellationToken.None);

        var saved = Assert.Single(repository.Terms);
        Assert.Equal(85, saved.Percent);
    }

    [Fact]
    public void Unknown_wording_becomes_effort_map_questions_after_the_apps_own_fields_and_is_capped()
    {
        var questionnaire = InterviewQuestions.Compose("""{"questions":[],"efforts":[]}""", ["a", "b", "c", "d", "e"]);

        var questions = InterviewQuestions.Parse(questionnaire);

        Assert.Equal(
            [InterviewFields.ChangedId, InterviewFields.DateId, InterviewFields.CourseId, "effort:a", "effort:b", "effort:c", "effort:d"],
            questions.Select(q => q.Id));
        var effort = questions[^1];
        Assert.Equal(InterviewQuestionKind.Field, effort.Kind);
        Assert.Equal(InterviewFields.EffortMapType, effort.FieldType);
        Assert.Contains("easy", effort.Options);
        Assert.Contains("a2", effort.Options);
    }

    [Fact]
    public void The_efforts_the_model_listed_are_read_and_a_questionnaire_without_any_is_fine()
    {
        Assert.Equal(["Wolf pace", "A2"], EffortQuestions.Read("""{"questions":[],"efforts":["Wolf pace","","A2"]}"""));
        Assert.Empty(EffortQuestions.Read("""{"questions":[]}"""));
    }

    [Fact]
    public void Answers_come_back_with_the_wording_they_are_for_and_skipped_ones_are_left_out()
    {
        var questionnaire = InterviewQuestions.Compose("""{"questions":[],"efforts":[]}""", ["Wolf pace", "Zone 4"]);
        var answers = InterviewQuestions.FormatAnswer(questionnaire, ["1", "2026-09-21", "1", "85%", ""]);

        var answered = EffortQuestions.Answered(questionnaire, answers);

        Assert.Equal([("Wolf pace", "85%")], answered);
    }

    [Fact]
    public void Effort_answers_are_not_shown_to_the_model_that_writes_the_session()
    {
        var questionnaire = InterviewQuestions.Compose(
            """{"questions":[{"id":"q1","q":"Which lane?","kind":"fork","opts":["Lane 1"],"showIf":null}],"efforts":[]}""", ["Wolf pace"]);
        var answers = InterviewQuestions.FormatAnswer(questionnaire, ["1", "1", "2026-09-21", "1", "85%"]);

        var transcript = InterviewTranscript.Build([(questionnaire, answers)]);

        Assert.DoesNotContain("Wolf pace", transcript);
    }
}
