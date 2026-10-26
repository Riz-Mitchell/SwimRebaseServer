using System.Text.Json;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using SessionConversion.Application.Interview;

namespace SwimRebase.Tests.SessionConversion;

public class ConversionSchemasTests
{
    /// <summary>The reported board, as the model is now constrained to write it: 12x50 fins as (3 easy, 1 as 25 fast + 25 easy) is a group.</summary>
    private const string ReportedBoard = """
        {"ledger":[
          {"boardLine":"300 Kick","outcome":"swum"},
          {"boardLine":"300 Swim Snorkel","outcome":"swum"},
          {"boardLine":"6x50 drills","outcome":"swum"},
          {"boardLine":"12x50 fins as 3 easy, 1 as 25 race speed 25 easy","outcome":"swum"},
          {"boardLine":"2x25 sub max Dive","outcome":"swum"}],
         "group":null,
         "sets":[
          {"kind":"item","reps":1,"timing":null,"parts":[{"dist":300,"stroke":"free","mode":"kick","equip":[],"effort":null}],"note":null},
          {"kind":"item","reps":1,"timing":null,"parts":[{"dist":300,"stroke":"free","mode":"swim","equip":["snorkel"],"effort":null}],"note":null},
          {"kind":"item","reps":6,"timing":null,"parts":[{"dist":50,"stroke":"choice","mode":"drill","equip":[],"effort":null}],"note":"drills"},
          {"kind":"group","repeat":3,"items":[
            {"kind":"item","reps":3,"timing":null,"parts":[{"dist":50,"stroke":"free","mode":"swim","equip":["fins"],"effort":"easy"}],"note":null},
            {"kind":"item","reps":1,"timing":null,"parts":[
              {"dist":25,"stroke":"free","mode":"swim","equip":["fins"],"effort":"race pace"},
              {"dist":25,"stroke":"free","mode":"swim","equip":["fins"],"effort":"easy"}],"note":null}]},
          {"kind":"item","reps":2,"timing":null,"parts":[{"dist":25,"stroke":"free","mode":"swim","equip":[],"effort":"sub max"}],"note":"dive"}]}
        """;

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        yield return element;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                foreach (var child in Walk(property.Value)) yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var child in Walk(item)) yield return child;
            }
        }
    }

    public static TheoryData<string> Schemas => new() { ConversionSchemas.Questionnaire, ConversionSchemas.Session };

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Every_object_is_closed_with_all_properties_required_and_no_unsupported_keywords(string schema)
    {
        using var document = JsonDocument.Parse(schema);

        foreach (var node in Walk(document.RootElement).Where(e => e.ValueKind == JsonValueKind.Object))
        {
            if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                Assert.False(node.GetProperty("additionalProperties").GetBoolean());
                var properties = node.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();
                Assert.Equal(properties, node.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order().ToArray());
            }

            foreach (var forbidden in new[] { "minItems", "maxItems", "minimum", "maximum", "minLength", "maxLength", "multipleOf" })
            {
                Assert.False(node.TryGetProperty(forbidden, out _), $"{forbidden} is not supported by structured output");
            }
        }
    }

    [Fact]
    public void The_session_schema_has_no_recursion_and_is_as_deep_as_the_validator_allows()
    {
        using var document = JsonDocument.Parse(ConversionSchemas.Session);
        var defs = document.RootElement.GetProperty("$defs");

        var references = defs.EnumerateObject().ToDictionary(
            d => d.Name,
            d => Walk(d.Value).Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("$ref", out _))
                .Select(e => e.GetProperty("$ref").GetString()!["#/$defs/".Length..]).ToHashSet());

        bool Cycles(string name, HashSet<string> path)
        {
            if (!path.Add(name)) return true;
            var cyclic = references[name].Any(next => Cycles(next, [.. path]));
            return cyclic;
        }

        Assert.All(references.Keys, name => Assert.False(Cycles(name, [])));
        for (var depth = 1; depth <= SessionRules.MaxGroupDepth; depth++)
        {
            Assert.True(defs.TryGetProperty($"group{depth}", out _));
        }

        Assert.False(defs.TryGetProperty($"group{SessionRules.MaxGroupDepth + 1}", out _));
    }

    [Fact]
    public void The_ledger_is_written_before_the_session_and_the_title_after_it()
    {
        using var document = JsonDocument.Parse(ConversionSchemas.Session);

        Assert.Equal(["ledger", "group", "sets", "title"], document.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void The_reported_board_reads_back_as_the_full_session()
    {
        var session = TestSessions.Ingest().Parse(ReportedBoard);
        var prepared = TestSessions.Ingest().Prepare(session);

        Assert.Equal(5, prepared.Sets.Count);
        var group = Assert.IsType<SetGroup>(prepared.Sets[3]);
        Assert.Equal(3, group.Repeat);
        Assert.Equal(2, group.Items.Count);
        var total = SessionCalculator.Total(prepared);
        Assert.Equal(300 + 300 + 300 + 3 * (3 * 50 + 50) + 50, total);
    }

    [Fact]
    public void An_item_can_carry_a_descend_in_both_the_conversion_schema_and_the_edit_tool()
    {
        using var conversion = JsonDocument.Parse(ConversionSchemas.Session);
        var defs = conversion.RootElement.GetProperty("$defs");

        Assert.Equal(["span", "from", "to"], defs.GetProperty("progression").GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.True(defs.GetProperty("item").GetProperty("properties").TryGetProperty("progression", out _));

        using var edit = JsonDocument.Parse(InterviewTools.EditDraftSchema);
        Assert.True(edit.RootElement.GetProperty("$defs").GetProperty("node").GetProperty("properties").TryGetProperty("progression", out _));
        Assert.True(edit.RootElement.GetProperty("$defs").TryGetProperty("progression", out _));
    }

    [Fact]
    public void A_descend_the_model_writes_is_read_and_filled_from_the_swimmers_settings()
    {
        const string board = """
            {"ledger":[],"group":null,"title":null,"sets":[
              {"kind":"item","reps":8,"timing":null,"parts":[{"dist":100,"stroke":"free","mode":"swim","equip":[],"effort":null}],
               "progression":{"span":4,"from":null,"to":"TH"},"note":null}]}
            """;

        var prepared = TestSessions.Ingest().Prepare(TestSessions.Ingest().Parse(board));

        var progression = Assert.IsType<SetItem>(prepared.Sets[0]).Progression!;
        Assert.Equal((4, 50, 83), (progression.Span, progression.FromIntensity!.Percent, progression.ToIntensity!.Percent));
    }
}
