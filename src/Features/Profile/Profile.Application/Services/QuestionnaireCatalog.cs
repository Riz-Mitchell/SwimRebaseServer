namespace Profile.Application.Services;

public sealed record QuestionOption(string Value, string Label);

public sealed record QuestionDefinition(string Key, string Prompt, IReadOnlyList<QuestionOption> Options);

/// <summary>
/// The onboarding questions. Answers are stored by key, so adding an entry here is all a new
/// question needs - users who haven't finished onboarding are asked it automatically.
/// </summary>
public static class QuestionnaireCatalog
{
    public static readonly IReadOnlyList<QuestionDefinition> Questions =
    [
        new("level", "What level are you?",
        [
            new("Regional", "Regional"),
            new("StateQualified", "State qualified"),
            new("NationalQualified", "National qualified"),
            new("OlympicTrialQualified", "Olympic trial qualified"),
        ]),
        new("goal", "What do you want out of SwimRebase?",
        [
            new("CompetitiveImprovement", "Competitive improvement"),
            new("DataInsights", "Data insights"),
            new("GeneralFitness", "General fitness"),
            new("SocialCommunityAndGamification", "Social, community and gamification"),
            new("Other", "Other"),
        ]),
    ];
}
