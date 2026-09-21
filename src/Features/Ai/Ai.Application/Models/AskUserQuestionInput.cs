using System.Text.Json;

namespace Ai.Application.Models;

/// <summary>Names of tools the Application layer itself refers to.</summary>
public static class AiToolNames
{
    public const string AskUserQuestion = "ask_user_question";
}

/// <summary>Parses the <see cref="AiToolNames.AskUserQuestion"/> tool's input JSON into the <see cref="AiChatQuestionAsked"/> event it raises.</summary>
public sealed record AskUserQuestionInput(string Question, IReadOnlyList<string> Options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AskUserQuestionInput Parse(string inputJson)
    {
        var payload = JsonSerializer.Deserialize<Payload>(inputJson, JsonOptions);
        return new AskUserQuestionInput(payload?.Question ?? string.Empty, (IReadOnlyList<string>?)payload?.Options ?? Array.Empty<string>());
    }

    private sealed record Payload(string? Question, List<string>? Options);
}
