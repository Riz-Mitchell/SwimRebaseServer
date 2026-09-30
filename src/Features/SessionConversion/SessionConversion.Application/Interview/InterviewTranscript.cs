using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SessionConversion.Application.Interview;

/// <summary>Turns the swimmer's questionnaire and its formatted answers back into a short "Q -> A" record, and reads single answers out of it.</summary>
public static partial class InterviewTranscript
{
    private const string NoAnswer = "(no answer)";

    public static string Build(IEnumerable<(string InputJson, string? Result)> exchanges)
    {
        var sb = new StringBuilder();
        foreach (var (inputJson, result) in exchanges)
        {
            foreach (var (question, answer) in Answers(inputJson, result))
            {
                if (question.Kind == InterviewQuestionKind.Field)
                {
                    continue;
                }

                sb.Append("- ").Append(question.Text).Append(" -> ").AppendLine(answer);
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The answer to one question, or null when it was not shown, not answered or skipped.</summary>
    public static string? AnswerFor(string inputJson, string? result, string questionId)
    {
        var answer = Answers(inputJson, result).FirstOrDefault(a => a.Question.Id == questionId).Answer;
        return string.IsNullOrWhiteSpace(answer) || answer is "(skipped)" or NoAnswer ? null : answer;
    }

    /// <summary>The date the swimmer gave, if it reads as one.</summary>
    public static DateOnly? SwamOn(string inputJson, string? result) =>
        AnswerFor(inputJson, result, InterviewFields.DateId) is { } text
        && DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>The pool course the swimmer gave (scy, scm or lcm), if it is one.</summary>
    public static string? Course(string inputJson, string? result) =>
        AnswerFor(inputJson, result, InterviewFields.CourseId) is { } text
            ? InterviewFields.Courses.FirstOrDefault(c => string.Equals(c, text.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Each question that applied, with its answer; a call nobody has answered yet yields every question as "(no answer)".</summary>
    private static IEnumerable<(InterviewQuestion Question, string Answer)> Answers(string inputJson, string? result)
    {
        var questions = InterviewQuestions.Parse(inputJson);
        if (string.IsNullOrWhiteSpace(result))
        {
            return questions.Select(q => (q, NoAnswer));
        }

        var answers = ParseAnswers(result);
        return questions.Select((q, i) => (Question: q, Number: i + 1))
            .Where(x => answers.ContainsKey(x.Number))
            .Select(x => (x.Question, answers[x.Number]));
    }

    private static Dictionary<int, string> ParseAnswers(string result)
    {
        var answers = new Dictionary<int, string>();
        foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = NumberedLine().Match(line);
            if (match.Success)
            {
                answers[int.Parse(match.Groups[1].Value)] = match.Groups[2].Value;
            }
        }

        return answers;
    }

    [GeneratedRegex(@"^(\d+)\.\s(.*)$")]
    private static partial Regex NumberedLine();
}
