using Ai.Domain.Models;

namespace Ai.Application.Models;

/// <summary>
/// Maps stored, 3-role <see cref="ConversationMessage"/> history (User/Assistant/Tool) onto the
/// 2-role shape the model API expects: a <c>Tool</c> row's content blocks are tool_result blocks,
/// which the API only accepts folded into a following User message, never as their own role.
/// </summary>
public static class ChatContextMapper
{
    public static IReadOnlyList<ChatMessage> ToChatMessages(IReadOnlyList<ConversationMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var message in messages.OrderBy(m => m.SequenceNumber))
        {
            var blocks = ChatContentJson.Deserialize(message.RawContentJson);

            switch (message.Role)
            {
                case MessageRole.User or MessageRole.Tool:
                    result.Add(new ChatMessage(ChatRole.User, blocks));
                    break;
                case MessageRole.Assistant:
                    result.Add(new ChatMessage(ChatRole.Assistant, blocks));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Swaps each image for a short text placeholder once an image-reading tool has answered after it: the answer (a draft, a summary)
    /// carries what the image said, and an image left in the history is re-sent, and re-billed, on every later turn. Pure and
    /// deterministic, so the same history always maps to the same context and the provider's prompt cache keeps hitting. The stored
    /// messages are untouched, so the image can still be read again by the tool.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ReplaceFinishedImages(IReadOnlyList<ChatMessage> messages, IReadOnlySet<string> imageToolNames)
    {
        if (imageToolNames.Count == 0)
        {
            return messages;
        }

        var lastImageToolResultIndex = -1;
        var imageToolUseIds = new HashSet<string>();
        for (var i = 0; i < messages.Count; i++)
        {
            foreach (var block in messages[i].Content)
            {
                switch (block)
                {
                    case ToolUseContentBlock use when imageToolNames.Contains(use.ToolName):
                        imageToolUseIds.Add(use.Id);
                        break;
                    case ToolResultContentBlock answer when imageToolUseIds.Contains(answer.ToolUseId):
                        lastImageToolResultIndex = i;
                        break;
                }
            }
        }

        if (lastImageToolResultIndex < 0)
        {
            return messages;
        }

        var result = new List<ChatMessage>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            result.Add(i < lastImageToolResultIndex && message.Content.Any(b => b is ImageContentBlock)
                ? message with { Content = message.Content.Select(b => b is ImageContentBlock ? Placeholder : b).ToList() }
                : message);
        }

        return result;
    }

    private static readonly TextContentBlock Placeholder = new("[Image attached earlier; it has already been read.]");
}
