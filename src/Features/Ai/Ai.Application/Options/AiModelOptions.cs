namespace Ai.Application.Options;

public sealed class AiModelOptions
{
    public const string SectionName = "Ai:Model";

    public string ChatModel { get; set; } = "claude-sonnet-5";

    public int MaxTokens { get; set; } = 16000;
}
