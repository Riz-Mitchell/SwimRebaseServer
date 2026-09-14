using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace Ai.Infrastructure.Chat;

/// <summary>
/// Builds the SDK request for one model turn and hands the raw event stream to
/// <see cref="AnthropicStreamProcessor"/> for translation into <see cref="ChatStreamEvent"/>s.
/// Never loops across tool calls itself - see <c>ChatTurnLoop</c> for that.
/// </summary>
public sealed class AnthropicChatClient(AnthropicClient client, IOptions<AiModelOptions> modelOptions) : IAiChatClient
{
    public IAsyncEnumerable<ChatStreamEvent> StreamTurnAsync(ChatTurnRequest request, CancellationToken cancellationToken)
    {
        var options = modelOptions.Value;
        var modelName = request.Model ?? options.ChatModel;

        var parameters = new MessageCreateParams
        {
            Model = modelName,
            MaxTokens = request.MaxTokens ?? options.MaxTokens,
            Messages = request.Messages.Select(AnthropicMessageMapper.ToMessageParam).ToList(),
            System = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt!,
            Tools = request.Tools.Count == 0
                ? null
                : request.Tools
                    .Select((tool, index) => AnthropicMessageMapper.ToToolUnion(tool, cache: index == request.Tools.Count - 1))
                    .ToList(),
        };

        var events = client.Messages.CreateStreaming(parameters);
        return new AnthropicStreamProcessor(modelName, request.Tools).ProcessAsync(events, cancellationToken);
    }
}
