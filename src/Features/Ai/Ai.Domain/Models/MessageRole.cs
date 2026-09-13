namespace Ai.Domain.Models;

public enum MessageRole
{
    User = 0,
    Assistant = 1,

    /// <summary>A tool_result turn sent back to the model on behalf of the user/system.</summary>
    Tool = 2,
}
