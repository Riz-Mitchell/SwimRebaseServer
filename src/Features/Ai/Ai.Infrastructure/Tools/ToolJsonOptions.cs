using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ai.Infrastructure.Tools;

/// <summary>Shared (de)serialization options for tool input/output: camelCase, case-insensitive, enums as strings.</summary>
internal static class ToolJsonOptions
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
