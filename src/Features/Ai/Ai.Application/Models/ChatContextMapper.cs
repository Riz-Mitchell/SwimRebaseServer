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
}
