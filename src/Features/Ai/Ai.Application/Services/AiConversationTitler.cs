using System.Text;
using System.Text.RegularExpressions;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ai.Application.Services;

/// <summary>A single no-tools call on a cheap model, like <see cref="ChatCompactionService"/>'s summary.</summary>
public sealed partial class AiConversationTitler(
    IAiChatClient chatClient,
    IOptions<AiTitleOptions> options,
    ILogger<AiConversationTitler>? logger = null) : IConversationTitler
{
    public const int MaxLength = 60;

    private const int MaxMessageLength = 1000;

    private const string SystemPrompt =
        "You name chats in a swimming coaching app. You get the first message of a chat. Reply with a title of 2 to 6 words that says what the " +
        "chat is about, in the same language as the message. Reply with the title only: no quotes, no trailing punctuation, no preamble.";

    private readonly ILogger<AiConversationTitler> _logger = logger ?? NullLogger<AiConversationTitler>.Instance;

    public async Task<TitleResult> GenerateAsync(string firstMessage, bool hasImages, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || (string.IsNullOrWhiteSpace(firstMessage) && !hasImages))
        {
            return TitleResult.None;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        try
        {
            var request = new ChatTurnRequest(
                [new ChatMessage(ChatRole.User, [new TextContentBlock(Describe(firstMessage, hasImages))])],
                SystemPrompt,
                Tools: [],
                Model: settings.Model,
                MaxTokens: settings.MaxTokens);

            var reply = new StringBuilder();
            ChatTokenUsage? usage = null;
            await foreach (var chatEvent in chatClient.StreamTurnAsync(request, timeout.Token))
            {
                switch (chatEvent)
                {
                    case TextDeltaEvent delta:
                        reply.Append(delta.Text);
                        break;
                    case TurnCompletedEvent completed:
                        usage = completed.Usage;
                        break;
                    case ErrorEvent error:
                        _logger.LogWarning("AI chat title call failed: {Message}", error.Message);
                        return TitleResult.None;
                }
            }

            return new TitleResult(Sanitize(reply.ToString()), usage);
        }
        catch (Exception ex)
        {
            // A title is a nicety: whatever went wrong (a timeout included), the chat carries on with the title it already has.
            _logger.LogWarning(ex, "AI chat title call did not complete");
            return TitleResult.None;
        }
    }

    /// <summary>The first line of the reply with quotes, markdown and a "Title:" label removed, cut to <see cref="MaxLength"/>; null when nothing is left.</summary>
    public static string? Sanitize(string? reply)
    {
        var line = (reply ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        line = line.Trim('"', '\'', '“', '”', '‘', '’', '`', '*', '#', ' ');
        line = LabelPattern().Replace(line, string.Empty);
        line = line.Trim('"', '\'', '“', '”', '‘', '’', '`', '*', ' ').TrimEnd('.', ',', ';', ':', '!', ' ');
        line = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (line.Length > MaxLength)
        {
            var cut = line[..MaxLength];
            var lastSpace = cut.LastIndexOf(' ');
            line = (lastSpace > MaxLength / 2 ? cut[..lastSpace] : cut).TrimEnd('.', ',', ';', ':', ' ');
        }

        return line.Length == 0 ? null : line;
    }

    private static string Describe(string firstMessage, bool hasImages)
    {
        var message = firstMessage.Trim();
        if (message.Length > MaxMessageLength)
        {
            message = message[..MaxMessageLength];
        }

        var sb = new StringBuilder("First message of the chat:\n").Append(message.Length == 0 ? "(no text)" : message);
        if (hasImages)
        {
            sb.Append("\n[the user attached an image]");
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^(chat\s+)?title\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();
}
