namespace Ai.Application.Options;

public sealed class AiTitleOptions
{
    public const string SectionName = "Ai:Titles";

    /// <summary>When off, chats keep the title cut from their first message.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cheap model used to name a chat - never the user-facing chat model.</summary>
    public string Model { get; set; } = "claude-haiku-4-5";

    public int MaxTokens { get; set; } = 40;

    /// <summary>The title is only worth having if it arrives with the reply; past this the chat keeps its first title.</summary>
    public int TimeoutSeconds { get; set; } = 8;
}
