using System.Runtime.CompilerServices;
using System.Text;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace Ai.Infrastructure.Chat;

/// <summary>
/// Consumes one raw Anthropic stream and translates it into <see cref="ChatStreamEvent"/>s -
/// accumulating text, tool_use blocks (which arrive as fragmented partial_json deltas), and usage
/// as it goes, then emitting a single <see cref="TurnCompletedEvent"/> at the end. One instance
/// per turn - all accumulation state is instance-local.
/// </summary>
internal sealed class AnthropicStreamProcessor(string modelName, IReadOnlyList<AiToolSpec> tools, ILogger logger)
{
    private readonly StringBuilder _textBuffer = new();
    private readonly List<ChatContentBlock> _assistantBlocks = [];
    private readonly Dictionary<long, StringBuilder> _pendingToolInputs = [];
    private readonly Dictionary<long, (string Id, string Name)> _pendingToolMeta = [];
    private string _stopReason = "end_turn";
    private long _inputTokens;
    private long _outputTokens;
    private long _cacheCreationInputTokens;
    private long _cacheReadInputTokens;

    public async IAsyncEnumerable<ChatStreamEvent> ProcessAsync(
        IAsyncEnumerable<RawMessageStreamEvent> events, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var enumerator = events.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            RawMessageStreamEvent? current = null;
            Exception? failure = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                {
                    break;
                }

                current = enumerator.Current;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (failure is not null)
            {
                logger.LogError(failure, "[{ClassName}] The {Model} stream failed", nameof(AnthropicStreamProcessor), modelName);
                yield return new ErrorEvent(failure.Message);
                yield break;
            }

            if (current is null)
            {
                continue;
            }

            if (TryHandleTextDelta(current, out var text))
            {
                yield return new TextDeltaEvent(text);
            }
            else
            {
                HandleNonTextEvent(current);
            }
        }

        yield return BuildTurnCompletedEvent();
    }

    private bool TryHandleTextDelta(RawMessageStreamEvent current, out string text)
    {
        if (current.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickText(out var textDelta))
        {
            _textBuffer.Append(textDelta.Text);
            text = textDelta.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    private void HandleNonTextEvent(RawMessageStreamEvent current)
    {
        if (current.TryPickContentBlockStart(out var blockStart) && blockStart.ContentBlock.TryPickToolUse(out var toolUse))
        {
            _pendingToolMeta[blockStart.Index] = (toolUse.ID, toolUse.Name);
            _pendingToolInputs[blockStart.Index] = new StringBuilder();
        }
        else if (current.TryPickContentBlockDelta(out var blockDelta) && blockDelta.Delta.TryPickInputJson(out var jsonDelta))
        {
            if (!_pendingToolInputs.TryGetValue(blockDelta.Index, out var sb))
            {
                sb = new StringBuilder();
                _pendingToolInputs[blockDelta.Index] = sb;
            }

            sb.Append(jsonDelta.PartialJson);
        }
        else if (current.TryPickContentBlockStop(out var blockStop) && _pendingToolMeta.TryGetValue(blockStop.Index, out var meta))
        {
            var inputJson = _pendingToolInputs.TryGetValue(blockStop.Index, out var sb) ? sb.ToString() : string.Empty;
            inputJson = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson;

            // Repaired here, before the call is stored, so the corrected input is also what later turns replay to the model.
            if (tools.FirstOrDefault(t => t.Name == meta.Name) is { } spec)
            {
                inputJson = ToolInputRepair.Repair(inputJson, spec.JsonSchema);
            }
            else
            {
                logger.LogWarning("[{ClassName}] The model called '{ToolName}', which is not among this turn's tools", nameof(AnthropicStreamProcessor), meta.Name);
            }

            _assistantBlocks.Add(new ToolUseContentBlock(meta.Id, meta.Name, inputJson));
        }
        else if (current.TryPickStart(out var messageStart))
        {
            _inputTokens = messageStart.Message.Usage.InputTokens;
            _cacheCreationInputTokens = messageStart.Message.Usage.CacheCreationInputTokens ?? 0;
            _cacheReadInputTokens = messageStart.Message.Usage.CacheReadInputTokens ?? 0;
        }
        else if (current.TryPickDelta(out var messageDelta))
        {
            if (messageDelta.Delta.StopReason is { } sr)
            {
                _stopReason = sr.Raw();
            }

            _outputTokens = messageDelta.Usage.OutputTokens;
        }
    }

    private TurnCompletedEvent BuildTurnCompletedEvent()
    {
        if (_textBuffer.Length > 0)
        {
            _assistantBlocks.Insert(0, new TextContentBlock(_textBuffer.ToString()));
        }

        var toolSpecsByName = tools.ToDictionary(t => t.Name);
        var toolCalls = _assistantBlocks
            .OfType<ToolUseContentBlock>()
            .Select(b => new ToolCallRequestedEvent(
                b.Id, b.ToolName, b.InputJson, toolSpecsByName.TryGetValue(b.ToolName, out var spec) && spec.RequiresApproval))
            .ToList();

        return new TurnCompletedEvent(
            _stopReason,
            toolCalls,
            _textBuffer.ToString(),
            ChatContentJson.Serialize(_assistantBlocks),
            new ChatTokenUsage(modelName, _inputTokens, _outputTokens, _cacheCreationInputTokens, _cacheReadInputTokens));
    }
}
