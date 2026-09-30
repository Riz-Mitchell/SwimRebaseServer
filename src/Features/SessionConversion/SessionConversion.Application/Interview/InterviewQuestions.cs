using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SessionConversion.Application.Interview;

public enum InterviewQuestionKind
{
    /// <summary>A choice inside one set.</summary>
    Detail,

    /// <summary>A branch to completely different sets; later questions can depend on its answer.</summary>
    Fork,

    /// <summary>A value the app asks for itself (the date, the pool) rather than one the model wrote.</summary>
    Field,
}

/// <summary>Shown only when the earlier question <paramref name="QuestionId"/> was answered with <paramref name="Option"/>.</summary>
public sealed record QuestionCondition(string QuestionId, string Option);

/// <param name="Options">Quick-reply suggestions; the swimmer can always type anything.</param>
/// <param name="FieldType">For <see cref="InterviewQuestionKind.Field"/> questions: which input the client shows (see <see cref="InterviewFields"/>).</param>
public sealed record InterviewQuestion(
    string Id, string Text, InterviewQuestionKind Kind, IReadOnlyList<string> Options, QuestionCondition? ShowIf = null, string? FieldType = null)
{
    public bool IsFork => Kind == InterviewQuestionKind.Fork;
}

public static class InterviewFields
{
    public const string ChangedId = "changed";
    public const string DateId = "performedOn";
    public const string CourseId = "course";

    public const string DateType = "date";
    public const string CourseType = "course";

    public static readonly string[] Courses = ["scy", "scm", "lcm"];
}

/// <summary>Reads the swimmer's questionnaire and turns their answers into text.</summary>
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

            var id = question.TryGetProperty("id", out var idElement) && idElement.GetString() is { Length: > 0 } given ? given : $"q{result.Count + 1}";
            var kind = (question.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null) switch
            {
                "fork" => InterviewQuestionKind.Fork,
                "field" => InterviewQuestionKind.Field,
                _ => InterviewQuestionKind.Detail,
            };
            var options = question.TryGetProperty("opts", out var opts) && opts.ValueKind == JsonValueKind.Array
                ? opts.EnumerateArray().Select(o => o.GetString() ?? string.Empty).Where(o => o.Length > 0).ToList()
                : [];
            var fieldType = question.TryGetProperty("fieldType", out var fieldElement) && fieldElement.ValueKind == JsonValueKind.String ? fieldElement.GetString() : null;

            result.Add(new InterviewQuestion(id, text, kind, options, ReadCondition(question), fieldType));
        }

        return result;
    }

    /// <summary>
    /// The stored questionnaire: what the model wrote, with any condition that points at an unknown or later question dropped (so the
    /// question is asked rather than never shown), then the fixed questions this app adds.
    /// </summary>
    public static string Compose(string modelJson)
    {
        var asked = new List<InterviewQuestion>();
        var seen = new HashSet<string>();
        foreach (var question in Parse(modelJson))
        {
            var id = seen.Add(question.Id) ? question.Id : $"{question.Id}_{asked.Count + 1}";
            seen.Add(id);
            var condition = question.ShowIf is { } c && asked.Any(a => a.Id == c.QuestionId) ? question.ShowIf : null;
            asked.Add(question with { Id = id, ShowIf = condition, Kind = question.Kind == InterviewQuestionKind.Field ? InterviewQuestionKind.Detail : question.Kind });
        }

        asked.Add(new InterviewQuestion(
            InterviewFields.ChangedId, "Did you do everything as written, or skip, shorten, change or add anything?", InterviewQuestionKind.Detail,
            ["Everything as written", "I changed something"]));
        asked.Add(new InterviewQuestion(
            InterviewFields.DateId, "What date did you swim?", InterviewQuestionKind.Field, [], FieldType: InterviewFields.DateType));
        asked.Add(new InterviewQuestion(
            InterviewFields.CourseId, "Which pool?", InterviewQuestionKind.Field, InterviewFields.Courses, FieldType: InterviewFields.CourseType));

        var array = new JsonArray();
        foreach (var question in asked)
        {
            var node = new JsonObject
            {
                ["id"] = question.Id,
                ["q"] = question.Text,
                ["kind"] = question.Kind.ToString().ToLowerInvariant(),
                ["opts"] = new JsonArray(question.Options.Select(o => (JsonNode)JsonValue.Create(o)!).ToArray()),
            };
            if (question.ShowIf is { } condition)
            {
                node["showIf"] = new JsonObject { ["qid"] = condition.QuestionId, ["opt"] = condition.Option };
            }

            if (question.FieldType is not null)
            {
                node["fieldType"] = question.FieldType;
            }

            array.Add(node);
        }

        return new JsonObject { ["questions"] = array }.ToJsonString();
    }

    /// <summary>The questions the swimmer is shown. Which ones apply is decided by <see cref="VisibleAnswers"/>, since it depends on their answers.</summary>
    public static IReadOnlyList<InterviewQuestion> Asked(string inputJson) => Parse(inputJson);

    /// <summary>
    /// Pairs each question that applies with the swimmer's answer (empty when blank), in order. A question whose <see cref="InterviewQuestion.ShowIf"/>
    /// is not met - its fork was answered otherwise, or is itself hidden - is left out; the client hides the same ones.
    /// </summary>
    public static IReadOnlyList<(int Number, InterviewQuestion Question, string Answer)> VisibleAnswers(
        IReadOnlyList<InterviewQuestion> questions, IReadOnlyList<string> rawAnswers)
    {
        var answered = new Dictionary<string, string>();
        var result = new List<(int, InterviewQuestion, string)>();
        for (var i = 0; i < questions.Count; i++)
        {
            var question = questions[i];
            if (question.ShowIf is { } condition
                && !(answered.TryGetValue(condition.QuestionId, out var forkAnswer)
                     && string.Equals(forkAnswer, condition.Option, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var answer = ResolveAnswer(question.Options, i < rawAnswers.Count ? rawAnswers[i] : null);
            answered[question.Id] = answer;
            result.Add((i + 1, question, answer));
        }

        return result;
    }

    /// <summary>The tool_result: one numbered line per question that applied. Numbers are the question's position in the full list, so hidden ones leave gaps.</summary>
    public static string FormatAnswer(string inputJson, IReadOnlyList<string> answers)
    {
        var sb = new StringBuilder();
        foreach (var (number, _, answer) in VisibleAnswers(Parse(inputJson), answers))
        {
            sb.Append(number).Append(". ").AppendLine(answer.Length == 0 ? "(skipped)" : answer);
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

    private static QuestionCondition? ReadCondition(JsonElement question)
    {
        if (!question.TryGetProperty("showIf", out var showIf) || showIf.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = showIf.TryGetProperty("qid", out var qid) ? qid.GetString() : null;
        var option = showIf.TryGetProperty("opt", out var opt) ? opt.GetString() : null;
        return string.IsNullOrEmpty(id) || option is null ? null : new QuestionCondition(id, option);
    }
}
