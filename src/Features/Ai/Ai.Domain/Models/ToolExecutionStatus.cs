namespace Ai.Domain.Models;

public enum ToolExecutionStatus
{
    /// <summary>Read-only tools skip this state entirely and execute immediately.</summary>
    PendingApproval = 0,
    Approved = 1,
    Denied = 2,
    Executed = 3,
    Failed = 4,
}
