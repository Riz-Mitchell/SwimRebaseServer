namespace Ai.Application.Models;

/// <summary>
/// Out-parameter substitute for an async-iterator method (same pattern as <see cref="Services.ToolApprovalResolution"/>): filled in by
/// <see cref="Services.ChatTurnLoop.RunAsync"/> so the caller can decide what runs next once the stream ends.
/// </summary>
public sealed class TurnLoopOutcome
{
    /// <summary>A sub-agent call in this turn created a child conversation that should now start running.</summary>
    public Guid? ChildConversationId { get; set; }

    /// <summary>This conversation is a sub-agent's, and belongs to this tool call in its parent chat.</summary>
    public Guid? ParentToolExecutionRequestId { get; set; }

    /// <summary>The profile's terminal tool just succeeded - the conversation is finished.</summary>
    public bool TerminalToolCompleted { get; set; }

    /// <summary>The model ended a turn without calling any tool (only possible when the profile does not force tool use).</summary>
    public bool EndedWithoutToolCall { get; set; }
}
