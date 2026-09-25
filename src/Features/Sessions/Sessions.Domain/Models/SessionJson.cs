using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sessions.Domain.Models;

/// <summary>Serialization for <see cref="SwimSession"/>: camelCase, enums as camelCase strings, and tolerant of what a model emits (any property order, any case).</summary>
public static class SessionJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // The model may emit "kind" anywhere in the object, not first.
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException($"Deserialized {typeof(T).Name} was null.");

    public static T Deserialize<T>(JsonElement element) => Deserialize<T>(element.GetRawText());
}
