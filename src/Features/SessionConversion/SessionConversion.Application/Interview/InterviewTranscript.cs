using System.Text;
using System.Text.RegularExpressions;

namespace SessionConversion.Application.Interview;

/// <summary>Turns the interview's question calls and their formatted answers back into a short "Q -> A" record for the main chat's model to read.</summary>
public static partial class InterviewTranscript
{
    public static string Build(IEnumerable<(string InputJson, string? Result)> exchanges)
    {
        var sb = new StringBuilder();
        foreach (var (inputJson, result) in exchanges)
        {
            var answers = ParseAnswers(result);
            var asked = InterviewQuestions.Asked(inputJson);
            for (var i = 0; i < asked.Count; i++)
            {
                sb.Append("- ").Append(asked[i].Text).Append(" -> ").AppendLine(answers.GetValueOrDefault(i + 1, "(no answer)"));
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<int, string> ParseAnswers(string? result)
    {
        var answers = new Dictionary<int, string>();
        foreach (var line in (result ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
