using System.Text.Json;

namespace Ai.Application.Models;

/// <summary>
/// Serializes/deserializes a message's content blocks for storage in <c>ConversationMessage.RawContentJson</c>.
/// Must go through the <see cref="IReadOnlyList{ChatContentBlock}"/>-typed overloads so
/// <see cref="ChatContentBlock"/>'s polymorphic discriminator is actually applied - serializing a
/// concrete block type directly would silently drop it.
/// </summary>
public static class ChatContentJson
{
    // Stored rows can have been round-tripped through Postgres jsonb, which reorders keys, so the
    // "$type" discriminator isn't guaranteed to come first.
    private static readonly JsonSerializerOptions ReadOptions = new() { AllowOutOfOrderMetadataProperties = true };

    public static string Serialize(IReadOnlyList<ChatContentBlock> blocks) => JsonSerializer.Serialize(blocks);

    public static IReadOnlyList<ChatContentBlock> Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ChatContentBlock>>(json, ReadOptions) ?? [];
}
