namespace Profile.Domain.Models;

/// <summary>One answered questionnaire question. Rows rather than columns so new questions need no schema change.</summary>
public class ProfileAnswer
{
    public Guid UserId { get; set; }

    public string QuestionKey { get; set; } = "";

    public string AnswerValue { get; set; } = "";

    public DateTimeOffset AnsweredAt { get; set; }

    public UserProfile? UserProfile { get; set; }
}
