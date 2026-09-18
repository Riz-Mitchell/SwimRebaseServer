using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <summary>
/// A tool the model can call. Implementations live in Ai.Infrastructure (see the Training feature's
/// timetable tools for an example) and register themselves via DI - the orchestration and approval
/// pipeline never changes to accommodate a new one.
/// </summary>
public interface IAiTool
{
    string Name { get; }

    /// <summary>Short, present-tense label for streaming a live status to the client, e.g. "Creating a focus".</summary>
    string DisplayName { get; }

    /// <summary>Coarse classification of this tool's effect on the user's data, for UI iconography/copy.</summary>
    ToolEffect Effect { get; }

    string Description { get; }

    /// <summary>Raw JSON Schema for the tool's input, e.g. {"type":"object","properties":{...}}.</summary>
    string JsonSchema { get; }

    /// <summary>
    /// True for any write/create/delete/destructive action. Such calls are never auto-executed -
    /// they are persisted as a <c>ToolExecutionRequest</c> and wait for explicit user approval.
    /// Read-only tools return false and execute immediately.
    /// </summary>
    bool RequiresApproval { get; }

    /// <summary>
    /// Executes the tool for <paramref name="userId"/>. Implementations MUST scope every data
    /// access to this user id - never trust an id embedded in <paramref name="inputJson"/>.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken);
}
