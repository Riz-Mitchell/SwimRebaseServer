using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;
using Shared.Application.Storage;

namespace Ai.Infrastructure.Chat;

/// <summary>
/// Builds the SDK request for one model turn and hands the raw event stream to
/// <see cref="AnthropicStreamProcessor"/> for translation into <see cref="ChatStreamEvent"/>s.
/// Never loops across tool calls itself - see <c>ChatTurnLoop</c> for that.
/// </summary>
public sealed class AnthropicChatClient(
    AnthropicClient client, IOptions<AiModelOptions> modelOptions, IBlobStorageService blobStorage) : IAiChatClient
{
    public IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken)
    {
        var options = modelOptions.Value;
        var modelName = request.Model ?? options.ChatModel;
        var events = client.Messages.CreateStreaming(BuildParameters(request, options, blobStorage));
        return new AnthropicStreamProcessor(modelName, request.Tools).ProcessAsync(events, cancellationToken);
    }

    public static MessageCreateParams BuildParameters(ChatTurnRequest request, AiModelOptions options, IBlobStorageService blobStorage)
    {
        var modelName = request.Model ?? options.ChatModel;

        var messages = request.Messages.Select(m => AnthropicMessageMapper.ToMessageParam(m, blobStorage)).ToList();
        if (request.PromptCaching)
        {
            AnthropicMessageMapper.MarkLastBlockForCaching(messages);
        }

        var parameters = new MessageCreateParams
        {
            Model = modelName,
            MaxTokens = request.MaxTokens ?? options.MaxTokens,
            Messages = messages,
            System = ToSystem(request),
            Tools = request.Tools.Count == 0
                ? null
                : request.Tools
                    .Select((tool, index) => AnthropicMessageMapper.ToToolUnion(tool, cache: index == request.Tools.Count - 1))
                    .ToList(),
        };

        if (request.RequireToolUse && request.Tools.Count > 0)
        {
            // Forced tool_choice (any/tool) is rejected by newer models, so steer with the prompt instead. One tool call per turn:
            // parallel calls would leave several tool_uses to answer at once.
            parameters = parameters with { ToolChoice = new ToolChoiceAuto { DisableParallelToolUse = true } };
        }

        if (request.Effort is { } effort)
        {
            parameters = parameters with { OutputConfig = new OutputConfig { Effort = Enum.Parse<Effort>(effort, ignoreCase: true) } };
        }

        return parameters;
    }

    private static MessageCreateParamsSystem? ToSystem(ChatTurnRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return null;
        }

        // A cached system prompt has to be sent as a text block so it can carry the cache marker.
        return request.PromptCaching
            ? new List<TextBlockParam> { new() { Text = request.SystemPrompt, CacheControl = new CacheControlEphemeral() } }
            : request.SystemPrompt;
    }
}
