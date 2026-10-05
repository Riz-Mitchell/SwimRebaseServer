using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sessions.Domain.Models;

/// <summary>
/// How hard a part was swum, as a percent of maximum effort. Every external scale (words, zones, percentages) is mapped onto it by an
/// <see cref="Calculations.IIntensityMapper"/>, so metrics only ever compare like with like; named scales are views over it
/// (<see cref="Calculations.EffortScale"/>).
/// </summary>
[JsonConverter(typeof(IntensityJsonConverter))]
public sealed record Intensity
{
    public const int Min = 1;
    public const int Max = 100;

    public int Percent { get; init; }

    /// <summary>The scale the input was mapped from ("words", "zones", "percent"), so a later change to a mapping can be replayed.</summary>
    public string Scale { get; init; } = string.Empty;

    /// <summary>The same effort on the tens scale (1-10), written out for clients that still read it.</summary>
    public int Level => ToLevel(Percent);

    public static int ToLevel(int percent) => Math.Clamp((int)Math.Round(percent / 10.0, MidpointRounding.AwayFromZero), 1, 10);
}

/// <summary>Reads both the current <c>{ percent, scale }</c> shape and the earlier <c>{ level, scale }</c> one (a level is ten percent).</summary>
public sealed class IntensityJsonConverter : JsonConverter<Intensity>
{
    public override Intensity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("intensity must be an object.");
        }

        int? percent = null;
        int? level = null;
        var scale = string.Empty;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name.ToLowerInvariant())
            {
                case "percent" when property.Value.ValueKind == JsonValueKind.Number:
                    percent = property.Value.GetInt32();
                    break;
                case "level" when property.Value.ValueKind == JsonValueKind.Number:
                    level = property.Value.GetInt32();
                    break;
                case "scale" when property.Value.ValueKind == JsonValueKind.String:
                    scale = property.Value.GetString() ?? string.Empty;
                    break;
            }
        }

        return new Intensity { Percent = percent ?? (level is { } l ? l * 10 : 0), Scale = scale };
    }

    public override void Write(Utf8JsonWriter writer, Intensity value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("percent", value.Percent);
        writer.WriteString("scale", value.Scale);
        writer.WriteNumber("level", value.Level);
        writer.WriteEndObject();
    }
}
