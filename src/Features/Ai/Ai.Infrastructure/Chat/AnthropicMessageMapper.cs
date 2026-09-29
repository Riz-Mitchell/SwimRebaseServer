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
    /// Puts a cache breakpoint on the last block of the newest message. Anthropic caches everything up to and including a
    /// breakpoint, so a conversation that grows by one exchange per turn re-reads all earlier turns from cache and pays full price only for the new ones.
    /// </summary>
    public static void MarkLastBlockForCaching(List<MessageParam> messages)
    {
        // The SDK keeps a message's content as an immutable array, so the blocks are copied out, changed, and put back on a new message.
        if (messages.Count == 0 || messages[^1].Content.Value is not IEnumerable<ContentBlockParam> existing)
        {
            return;
        }

        var blocks = existing.ToList();
        if (blocks.Count == 0)
        {
            return;
        }

        var last = blocks[^1];
        if (last.TryPickText(out var text))
        {
            blocks[^1] = text with { CacheControl = new CacheControlEphemeral() };
        }
        else if (last.TryPickToolResult(out var result))
        {
            blocks[^1] = result with { CacheControl = new CacheControlEphemeral() };
        }
        else if (last.TryPickImage(out var image))
        {
            blocks[^1] = image with { CacheControl = new CacheControlEphemeral() };
        }
        else
        {
            return;
        }

        messages[^1] = messages[^1] with { Content = blocks };
    }

    /// <summary>
    /// Parses our tool's raw JSON Schema string into the SDK's typed <see cref="InputSchema"/>.
    /// The tool list is identical on every turn of a conversation (see <c>ChatTurnLoop</c>), so the
    /// last tool gets a cache breakpoint - Anthropic caches everything up to and including it.
    /// </summary>
    public static ToolUnion ToToolUnion(AiToolSpec spec, bool cache = false)
    {
        using var schema = JsonDocument.Parse(spec.JsonSchema);

        // Passed through whole (not just properties/required) so schemas that use $defs/$ref survive - a recursive
        // structure such as a tree of sets can only be described that way.
        return new Tool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = InputSchema.FromRawUnchecked(schema.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())),
            CacheControl = cache ? new CacheControlEphemeral() : null,
        };
    }
}
