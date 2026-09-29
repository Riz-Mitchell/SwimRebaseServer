using System.Text.Json;
using Ai.Application.Services;
using SessionConversion.Application.Interview;

namespace SwimRebase.Tests.Ai;

public class ToolInputRepairTests
{
    private const string Questions = """[{"q":"What stroke for the warm-up?","kind":"detail","opts":["all free","mixed"]},{"q":"Which lane?","kind":"fork","opts":[]}]""";

    private static string Stringified(string name, string json) => JsonSerializer.Serialize(new Dictionary<string, string> { [name] = json });

    [Fact]
    public void An_array_sent_as_a_json_string_is_turned_back_into_an_array()
    {
        var repaired = ToolInputRepair.Repair(Stringified("questions", Questions), InterviewTools.AskSchema);

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(repaired).RootElement.GetProperty("questions").ValueKind);
        Assert.Equal(2, InterviewQuestions.Parse(repaired).Count);
    }

    [Fact]
    public void Input_that_is_already_valid_is_returned_untouched()
    {
        var input = $$"""{"questions": {{Questions}} }""";

        Assert.Same(input, ToolInputRepair.Repair(input, InterviewTools.AskSchema));
    }

    [Fact]
    public void A_string_that_does_not_hold_the_right_kind_of_json_is_left_for_validation_to_reject()
    {
        var notJson = Stringified("questions", "which lane did you swim in?");
        var wrongKind = Stringified("questions", """{"q":"an object, not an array"}""");

        Assert.Equal(notJson, ToolInputRepair.Repair(notJson, InterviewTools.AskSchema));
        Assert.Equal(wrongKind, ToolInputRepair.Repair(wrongKind, InterviewTools.AskSchema));
    }

    [Fact]
    public void A_string_the_schema_expects_as_a_string_is_never_parsed()
    {
        const string schema = """{"type":"object","properties":{"note":{"type":"string"}}}""";
        var input = Stringified("note", "[1,2,3]");

        Assert.Same(input, ToolInputRepair.Repair(input, schema));
    }

    [Fact]
    public void Nested_values_are_repaired_through_refs_and_array_items()
    {
        var node = """{"kind":"item","reps":4,"parts":"[{\"dist\":100,\"stroke\":\"free\"}]"}""";
        var input = JsonSerializer.Serialize(new { draftId = "d1", edits = JsonSerializer.Serialize(new object[] { new { op = "insert", path = "1", node = node } }) });

        var repaired = JsonDocument.Parse(ToolInputRepair.Repair(input, InterviewTools.EditDraftSchema)).RootElement;

        var edit = repaired.GetProperty("edits")[0];
        Assert.Equal("insert", edit.GetProperty("op").GetString());
        var repairedNode = edit.GetProperty("node");
        Assert.Equal(JsonValueKind.Object, repairedNode.ValueKind);
        Assert.Equal(JsonValueKind.Array, repairedNode.GetProperty("parts").ValueKind);
        Assert.Equal(100, repairedNode.GetProperty("parts")[0].GetProperty("dist").GetInt32());
    }

    [Fact]
    public void Broken_input_or_schema_is_returned_as_it_came()
    {
        Assert.Equal("{not json", ToolInputRepair.Repair("{not json", InterviewTools.AskSchema));
        Assert.Equal("""{"a":1}""", ToolInputRepair.Repair("""{"a":1}""", "not a schema"));
    }
}
