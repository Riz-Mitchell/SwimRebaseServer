using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Anthropic.Models.Messages;
using ChatMessage = Ai.Application.Models.ChatMessage;

namespace Ai.Infrastructure.Chat;

/// <summary>Converts between our provider-agnostic <see cref="ChatContentBlock"/> shape and the Anthropic SDK's request types.</summary>
internal static class AnthropicMessageMapper
{
    public static MessageParam ToMessageParam(ChatMessage message) => new()
    {
        Role = message.Role == ChatRole.User ? Role.User : Role.Assistant,
        Content = message.Content.Select(ToContentBlockParam).ToList(),
    };

    public static ContentBlockParam ToContentBlockParam(ChatContentBlock block) => block switch
    {
        TextContentBlock text => new TextBlockParam { Text = text.Text },
        ToolUseContentBlock toolUse => new ToolUseBlockParam
        {
            ID = toolUse.Id,
            Name = toolUse.ToolName,
            Input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolUse.InputJson) ?? [],
        },
        ToolResultContentBlock toolResult => new ToolResultBlockParam
        {
            ToolUseID = toolResult.ToolUseId,
            Content = toolResult.ResultJson,
            IsError = toolResult.IsError,
        },
        _ => throw new NotSupportedException($"Unsupported content block '{block.GetType().Name}'."),
    };

    /// <summary>Parses our tool's raw JSON Schema string into the SDK's typed <see cref="InputSchema"/>.</summary>
    public static ToolUnion ToToolUnion(AiToolSpec spec)
    {
        using var schema = JsonDocument.Parse(spec.JsonSchema);
        var root = schema.RootElement;

        var properties = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("properties", out var propsElement))
        {
            foreach (var property in propsElement.EnumerateObject())
            {
                properties[property.Name] = property.Value.Clone();
            }
        }

        var required = root.TryGetProperty("required", out var requiredElement)
            ? requiredElement.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];

        return new Tool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = new InputSchema
            {
                Properties = properties,
                Required = required,
            },
        };
    }
}
