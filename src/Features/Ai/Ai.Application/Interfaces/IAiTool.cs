namespace Ai.Application.Interfaces;

/// <summary>
/// A tool the model can call. No swim-domain tools are implemented yet - this is purely the
/// contract + guard framework so tools can be dropped in later without touching the orchestration
/// or approval pipeline.
/// </summary>
public interface IAiTool
{
    string Name { get; }

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
    Task<string> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken);
}
