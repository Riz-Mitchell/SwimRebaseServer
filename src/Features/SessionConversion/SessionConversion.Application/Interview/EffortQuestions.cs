using System.Text.Json;
using Sessions.Domain.Calculations;

namespace SessionConversion.Application.Interview;

/// <summary>
/// The questions asking a swimmer what their own effort wording means ("Wolf pace"). The model only copies the wording off the board;
/// which of it is unknown is decided in code, and the answers are read back here to be saved against the swimmer.
/// </summary>
public static class EffortQuestions
{
    /// <summary>The question id is this followed by the wording, which is how the answer finds its way back to it.</summary>
    public const string IdPrefix = "effort:";

    /// <summary>The most wordings asked about in one questionnaire, so a board full of jargon does not bury the real questions.</summary>
    public const int MaxAsked = 4;

    private static readonly string[] Meanings = [.. EffortVocabulary.Default.Entries.OrderBy(e => e.Percent).Select(e => e.Phrases[0])];

    /// <summary>The effort wordings the model listed from the board, as written.</summary>
    public static IReadOnlyList<string> Read(string modelJson)
    {
        using var document = JsonDocument.Parse(modelJson);
        return document.RootElement.TryGetProperty("efforts", out var efforts) && efforts.ValueKind == JsonValueKind.Array
            ? efforts.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(e => e.Length > 0).ToList()
            : [];
    }

    public static IReadOnlyList<InterviewQuestion> Ask(IEnumerable<string> unknown) =>
        unknown
            .Take(MaxAsked)
            .Select(phrase => new InterviewQuestion(
                IdPrefix + phrase,
                $"How hard is \"{phrase}\"? Pick what it matches, or give a percent.",
                InterviewQuestionKind.Field,
                Meanings,
                FieldType: InterviewFields.EffortMapType))
            .ToList();

    /// <summary>Each wording the swimmer answered, with their answer. Skipped ones are left out.</summary>
    public static IReadOnlyList<(string Phrase, string Meaning)> Answered(string inputJson, string? result) =>
        InterviewQuestions.Parse(inputJson)
            .Where(q => q.FieldType == InterviewFields.EffortMapType && q.Id.StartsWith(IdPrefix, StringComparison.Ordinal))
            .Select(q => (Phrase: q.Id[IdPrefix.Length..], Meaning: InterviewTranscript.AnswerFor(inputJson, result, q.Id)))
            .Where(a => a.Meaning is not null)
            .Select(a => (a.Phrase, a.Meaning!))
            .ToList();
}
