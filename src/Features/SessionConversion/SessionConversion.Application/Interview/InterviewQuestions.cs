using System.Text;
using System.Text.Json;

namespace SessionConversion.Application.Interview;

/// <param name="IsFork">A branch to completely different sets (group/lane, A/B/C). Its answer decides which later questions exist.</param>
/// <param name="Options">Quick-reply suggestions; the swimmer can always type anything.</param>
public sealed record InterviewQuestion(string Text, bool IsFork, IReadOnlyList<string> Options);

/// <summary>Reads the interviewer's <c>ask</c> input and turns the swimmer's answers into the tool_result text.</summary>
public static class InterviewQuestions
{
    public static IReadOnlyList<InterviewQuestion> Parse(string inputJson)
    {
        using var document = JsonDocument.Parse(inputJson);
        if (!document.RootElement.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<InterviewQuestion>();
        foreach (var question in questions.EnumerateArray())
        {
            var text = question.TryGetProperty("q", out var q) ? q.GetString() ?? string.Empty : string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            var isFork = question.TryGetProperty("kind", out var kind) && kind.GetString() == "fork";
            var options = question.TryGetProperty("opts", out var opts) && opts.ValueKind == JsonValueKind.Array
                ? opts.EnumerateArray().Select(o => o.GetString() ?? string.Empty).Where(o => o.Length > 0).ToList()
                : [];
            result.Add(new InterviewQuestion(text, isFork, options));
        }

        return result;
    }

    /// <summary>
    /// A fork ends the turn: later questions depend on its answer, so anything after the first fork is held back
    /// (and the model is told to ask again next turn if it is still relevant).
    /// </summary>
    public static (IReadOnlyList<InterviewQuestion> Asked, int HeldBack) SplitAtFork(IReadOnlyList<InterviewQuestion> questions)
    {
        var fork = questions.ToList().FindIndex(q => q.IsFork);
        var asked = fork >= 0 ? questions.Take(fork + 1).ToList() : questions.ToList();
        return (asked, questions.Count - asked.Count);
    }

    /// <summary>The questions the swimmer is actually shown for this call.</summary>
    public static IReadOnlyList<InterviewQuestion> Asked(string inputJson) => SplitAtFork(Parse(inputJson)).Asked;

    /// <summary>The tool_result: one numbered line per question asked, plus notes about held-back questions and the turn cap.</summary>
    public static string FormatAnswer(string inputJson, IReadOnlyList<string> answers, bool lastChance)
    {
        var all = Parse(inputJson);
        if (all.Count == 0)
        {
            return "No questions received. If the path is complete call draft_session; otherwise ask.";
        }

        var (asked, heldBack) = SplitAtFork(all);
        var sb = new StringBuilder();
        for (var i = 0; i < asked.Count; i++)
        {
            var text = ResolveAnswer(asked[i].Options, i < answers.Count ? answers[i] : null);
            sb.Append(i + 1).Append(". ").AppendLine(text.Length == 0 ? "(skipped)" : text);
        }

        if (heldBack > 0)
        {
            sb.AppendLine($"{heldBack} later question(s) were not asked because they follow the fork; ask again next turn if still relevant.");
        }

        if (lastChance)
        {
            sb.AppendLine("Question limit reached: call draft_session now with your best reading.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>"2" picks the second option; otherwise the text is used as typed. Line breaks are flattened so one answer stays on one line.</summary>
    public static string ResolveAnswer(IReadOnlyList<string> options, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (int.TryParse(trimmed, out var number) && number >= 1 && number <= options.Count)
        {
            return options[number - 1];
        }

        var exact = options.FirstOrDefault(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase));
        return string.Join(' ', (exact ?? trimmed).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
