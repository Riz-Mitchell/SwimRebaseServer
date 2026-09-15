namespace Ai.Application.Services;

/// <summary>Derives a conversation's title from the user's first message - no model call involved.</summary>
public static class ConversationTitleGenerator
{
    private const int MaxLength = 60;

    public static string Generate(string initialMessage)
    {
        var trimmed = initialMessage.Trim();
        if (trimmed.Length == 0)
        {
            return "New conversation";
        }

        if (trimmed.Length <= MaxLength)
        {
            return trimmed;
        }

        var cut = trimmed[..MaxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd('.', ',', ';', ':') + "...";
    }
}
