using System.Text.Json;
using System.Text.Json.Nodes;
using Sessions.Domain.Models;

namespace SessionConversion.Application.Interview;

/// <summary>
/// The JSON Schemas the interview's two model calls are constrained to (structured output). The provider rejects recursive schemas,
/// so the session tree is written out level by level up to <see cref="SessionRules.MaxGroupDepth"/> - exactly as deep as the
/// validator allows. Every object is closed and every property required, with optional values as nullable ones.
/// </summary>
public static class ConversionSchemas
{
    public static readonly string[] Outcomes = ["swum", "modified", "skipped", "other_group", "added"];

    /// <summary>The swimmer's question list: flat, with later questions conditional on the answer to an earlier fork.</summary>
    public static readonly string Questionnaire = BuildQuestionnaire();

    /// <summary>What the swimmer swam: a ledger of every board line first, then the session tree itself.</summary>
    public static readonly string Session = BuildSession();

    private static string BuildQuestionnaire()
    {
        var condition = Closed(("qid", Text()), ("opt", Text()));

        var question = Closed(
            ("id", Text()),
            ("q", Text()),
            ("kind", EnumOf("fork", "detail")),
            ("opts", ArrayOf(Text())),
            ("showIf", OrNull(condition)));

        return Serialize(Closed(("questions", ArrayOf(question))));
    }

    private static string BuildSession()
    {
        var timing = Closed(("kind", EnumOf(Names<TimingKind>())), ("seconds", Integer()));

        var part = Closed(
            ("dist", Number()),
            ("stroke", EnumOf(Names<Stroke>())),
            ("mode", EnumOf(Names<SwimMode>())),
            ("equip", ArrayOf(Text())),
            ("effort", OrNull(Text())));

        var item = Closed(
            ("kind", EnumOf("item")),
            ("reps", Integer()),
            ("timing", OrNull(Ref("timing"))),
            ("parts", ArrayOf(Ref("part"))),
            ("note", OrNull(Text())));

        var defs = new JsonObject { ["timing"] = timing, ["part"] = part, ["item"] = item };
        for (var depth = 1; depth <= SessionRules.MaxGroupDepth; depth++)
        {
            var children = new JsonArray { Ref("item") };
            if (depth < SessionRules.MaxGroupDepth)
            {
                children.Add(Ref($"group{depth + 1}"));
            }

            defs[$"group{depth}"] = Closed(
                ("kind", EnumOf("group")),
                ("repeat", Integer()),
                ("items", ArrayOf(children.Count == 1 ? Ref("item") : new JsonObject { ["anyOf"] = children })));
        }

        var ledgerEntry = Closed(("boardLine", Text()), ("outcome", EnumOf(Outcomes)));

        var root = Closed(
            ("ledger", ArrayOf(ledgerEntry)),
            ("group", OrNull(Text())),
            ("sets", ArrayOf(new JsonObject { ["anyOf"] = new JsonArray { Ref("item"), Ref("group1") } })),
            ("title", OrNull(Text())));
        root["$defs"] = defs;

        return Serialize(root);
    }

    private static string[] Names<TEnum>() where TEnum : struct, System.Enum =>
        System.Enum.GetNames<TEnum>().Select(n => JsonNamingPolicy.CamelCase.ConvertName(n)).ToArray();

    private static JsonObject Text() => new() { ["type"] = "string" };

    private static JsonObject Integer() => new() { ["type"] = "integer" };

    private static JsonObject Number() => new() { ["type"] = "number" };

    private static JsonObject EnumOf(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
    };

    private static JsonObject ArrayOf(JsonNode items) => new() { ["type"] = "array", ["items"] = items };

    private static JsonObject Ref(string name) => new() { ["$ref"] = $"#/$defs/{name}" };

    private static JsonObject OrNull(JsonNode schema) => new()
    {
        ["anyOf"] = new JsonArray { schema, new JsonObject { ["type"] = "null" } },
    };

    /// <summary>Properties keep the order given: the model writes them in that order, so earlier ones can prime later ones.</summary>
    private static JsonObject Closed(params (string Name, JsonNode Schema)[] properties)
    {
        var props = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            props[name] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = new JsonArray(properties.Select(p => (JsonNode)JsonValue.Create(p.Name)!).ToArray()),
            ["additionalProperties"] = false,
        };
    }

    private static string Serialize(JsonNode node) => node.ToJsonString();
}
