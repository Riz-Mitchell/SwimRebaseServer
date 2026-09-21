namespace Ai.Domain.Models;

public enum ToolExecutionStatus
{
    /// <summary>Read-only tools skip this state entirely and execute immediately.</summary>
    PendingApproval = 0,
    Approved = 1,
    Denied = 2,
    Executed = 3,
    Failed = 4,

    /// <summary>A sub-agent tool call whose child conversation is still running (it may be waiting on the user). The parent conversation stays blocked until it resolves.</summary>
    InProgress = 5,
}
