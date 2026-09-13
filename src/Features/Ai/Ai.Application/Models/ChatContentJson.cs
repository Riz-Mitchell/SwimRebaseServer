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
    public static string Serialize(IReadOnlyList<ChatContentBlock> blocks) => JsonSerializer.Serialize(blocks);

    public static IReadOnlyList<ChatContentBlock> Deserialize(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<ChatContentBlock>>(json) ?? [];
}
