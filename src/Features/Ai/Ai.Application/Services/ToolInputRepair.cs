using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ai.Application.Services;

/// <summary>
/// Fixes one known model slip in a tool call's input: an array or object argument sent as a JSON-encoded string
/// (<c>"questions": "[{...}]"</c> instead of <c>"questions": [{...}]</c>). Guided by the tool's own JSON Schema, any value the schema says
/// is an array or object that arrives as a string holding one is replaced by the parsed value, at any depth. Anything else is left exactly as
/// the model sent it, so genuinely wrong input still fails validation downstream.
/// </summary>
public static class ToolInputRepair
{
    private const int MaxDepth = 32;

    /// <summary>The repaired input, or <paramref name="inputJson"/> itself, unchanged and not re-serialized, when nothing needed repair.</summary>
    public static string Repair(string inputJson, string schemaJson)
    {
        try
        {
            if (JsonNode.Parse(inputJson) is not { } input || JsonNode.Parse(schemaJson) is not JsonObject schema)
            {
                return inputJson;
            }

            var changed = false;
            var repaired = Fix(input, schema, schema["$defs"] as JsonObject, ref changed, depth: 0);
            return changed && repaired is not null ? repaired.ToJsonString() : inputJson;
        }
        catch (JsonException)
        {
            return inputJson;
        }
    }

    private static JsonNode? Fix(JsonNode? node, JsonNode? schemaNode, JsonObject? defs, ref bool changed, int depth)
    {
        if (node is null || schemaNode is not JsonObject declared || depth > MaxDepth)
        {
            return node;
        }

        var schema = Resolve(declared, defs);
        var type = (schema["type"] as JsonValue)?.TryGetValue<string>(out var t) == true ? t : null;

        if (node is JsonValue value && value.TryGetValue<string>(out var text) && (type == "array" || type == "object"))
        {
            if (TryParseAs(text, type) is not { } parsed)
            {
                return node;
            }

            node = parsed;
            changed = true;
        }

        if (node is JsonObject obj && schema["properties"] is JsonObject properties)
        {
            foreach (var name in properties.Select(p => p.Key).ToList())
            {
                if (obj[name] is { } child && Fix(child, properties[name], defs, ref changed, depth + 1) is { } fixedChild && !ReferenceEquals(fixedChild, child))
                {
                    obj[name] = fixedChild;
                }
            }
        }
        else if (node is JsonArray array && schema["items"] is JsonObject items)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is { } child && Fix(child, items, defs, ref changed, depth + 1) is { } fixedChild && !ReferenceEquals(fixedChild, child))
                {
                    array[i] = fixedChild;
                }
            }
        }

        return node;
    }

    /// <summary>Follows a local <c>$ref</c> such as <c>#/$defs/node</c>; anything else is returned as it is.</summary>
    private static JsonObject Resolve(JsonObject schema, JsonObject? defs)
    {
        const string prefix = "#/$defs/";
        return (schema["$ref"] as JsonValue)?.TryGetValue<string>(out var reference) == true
               && reference.StartsWith(prefix, StringComparison.Ordinal)
               && defs?[reference[prefix.Length..]] is JsonObject target
            ? target
            : schema;
    }

    private static JsonNode? TryParseAs(string text, string type)
    {
        try
        {
            var parsed = JsonNode.Parse(text.Trim());
            return type == "array" ? parsed as JsonArray : parsed as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
