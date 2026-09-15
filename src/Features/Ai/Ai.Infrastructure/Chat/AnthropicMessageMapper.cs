using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Anthropic.Models.Messages;
using Shared.Application.Storage;
using ChatMessage = Ai.Application.Models.ChatMessage;

namespace Ai.Infrastructure.Chat;

/// <summary>Converts between our provider-agnostic <see cref="ChatContentBlock"/> shape and the Anthropic SDK's request types.</summary>
internal static class AnthropicMessageMapper
{
    public static MessageParam ToMessageParam(ChatMessage message, IBlobStorageService blobStorage) => new()
    {
        Role = message.Role == ChatRole.User ? Role.User : Role.Assistant,
        Content = message.Content.Select(block => ToContentBlockParam(block, blobStorage)).ToList(),
    };

    public static ContentBlockParam ToContentBlockParam(ChatContentBlock block, IBlobStorageService blobStorage) => block switch
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
        // Resolved to a fresh presigned URL right here, at the moment of the actual API call - never
        // stored. The permanent ObjectKey is all that's kept in conversation history (see
        // ImageContentBlock), so this works identically whether the image was uploaded a minute ago
        // or is being replayed as history months into a long-running conversation.
        ImageContentBlock image => new ImageBlockParam
        {
            Source = new UrlImageSource { Url = blobStorage.CreatePresignedDownloadUrl(image.ObjectKey, ImageUploadOptions.DownloadUrlExpiry) },
        },
        _ => throw new NotSupportedException($"Unsupported content block '{block.GetType().Name}'."),
    };

    /// <summary>
    /// Parses our tool's raw JSON Schema string into the SDK's typed <see cref="InputSchema"/>.
    /// The tool list is identical on every turn of a conversation (see <c>ChatTurnLoop</c>), so the
    /// last tool gets a cache breakpoint - Anthropic caches everything up to and including it.
    /// </summary>
    public static ToolUnion ToToolUnion(AiToolSpec spec, bool cache = false)
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
            CacheControl = cache ? new CacheControlEphemeral() : null,
        };
    }
}
