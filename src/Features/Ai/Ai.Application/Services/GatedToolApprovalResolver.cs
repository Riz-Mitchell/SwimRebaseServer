using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services;

/// <summary>
/// Records how each kind of gated call was settled - approve/deny, a typed answer, a finished or cancelled sub-agent -
/// and hands the shared "settle the turn" work to <see cref="ToolTurnCloser"/>. Every method follows the same
/// sibling-wait rule: the model only resumes once every gated call the same assistant turn raised has been settled.
/// </summary>
public sealed class GatedToolApprovalResolver(
    ToolTurnCloser closer,
    IToolExecutionRepository toolExecutions,
    IConversationRepository conversations,
    IAiToolRegistry toolRegistry,
    IAgentProfileProvider profiles,
    IEnumerable<ISubAgent> subAgents,
    ILogger<GatedToolApprovalResolver>? logger = null)
{
    private readonly ILogger<GatedToolApprovalResolver> _logger = logger ?? NullLogger<GatedToolApprovalResolver>.Instance;

    public IAsyncEnumerable<AiChatEvent> ResolveAsync(
        Guid userId, ToolExecutionRequest request, bool approve, string? denialReason,
        ToolApprovalResolution resolution, CancellationToken cancellationToken) =>
        closer.ResolveAsync(userId, request, r =>
        {
            _logger.LogInformation("[{ClassName}] Tool request {ToolRequestId} ({ToolName}) {Decision}{Reason}",
                nameof(GatedToolApprovalResolver), r.Id, r.ToolName, approve ? "approved" : "denied", denialReason is null ? string.Empty : $": {denialReason}");
            r.Status = approve ? ToolExecutionStatus.Approved : ToolExecutionStatus.Denied;
            r.DenialReason = denialReason;
        }, resolution, cancellationToken);

    /// <summary>
    /// Records the user's answer(s) to a question tool. The request is marked <see cref="ToolExecutionStatus.Approved"/> (there is no
    /// approve/deny axis for a question) with the tool's formatted answer stashed in <see cref="ToolExecutionRequest.ResultJson"/>.
    /// </summary>
    public async IAsyncEnumerable<AiChatEvent> AnswerAsync(
        Guid userId, ToolExecutionRequest request, IReadOnlyList<string> answers,
        ToolApprovalResolution resolution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tool = toolRegistry.Find(request.ToolName) as IQuestionTool;
        if (tool is null)
        {
            _logger.LogError("[{ClassName}] Tool request {ToolRequestId} names '{ToolName}', which is not a question tool", nameof(GatedToolApprovalResolver), request.Id, request.ToolName);
            throw new InvalidOperationException($"Tool '{request.ToolName}' is not a question tool.");
        }

        var conversation = await conversations.FindAsync(userId, request.ConversationId, cancellationToken);
        var maxTurns = conversation is null ? null : profiles.Get(conversation.Kind).MaxQuestionTurns;
        var turn = await toolExecutions.CountForConversationAsync(request.ConversationId, request.ToolName, cancellationToken);
        var resultJson = tool.FormatAnswer(request.InputJson, answers, new QuestionAnswerContext(turn, maxTurns));
        _logger.LogInformation("[{ClassName}] Recorded answer to {ToolName} request {ToolRequestId} (turn {Turn}, max {MaxTurns})",
            nameof(GatedToolApprovalResolver), request.ToolName, request.Id, turn, maxTurns);

        await foreach (var chatEvent in closer.ResolveAsync(userId, request, r =>
        {
            r.Status = ToolExecutionStatus.Approved;
            r.ResultJson = resultJson;
        }, resolution, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    /// <summary>A sub-agent's conversation finished (or ended without a result): settle the parent tool call it was started from.</summary>
    public async IAsyncEnumerable<AiChatEvent> CompleteSubAgentAsync(
        Guid userId, Guid childConversationId, Guid parentRequestId, bool succeeded,
        ToolApprovalResolution resolution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parent = await toolExecutions.FindAsync(userId, parentRequestId, cancellationToken);
        if (parent is null || parent.Status != ToolExecutionStatus.InProgress)
        {
            _logger.LogWarning(
                "[{ClassName}] Sub-agent conversation {ChildConversationId} finished but its parent request {ParentRequestId} is {Status}, not InProgress; the parent chat is not resumed",
                nameof(GatedToolApprovalResolver), childConversationId, parentRequestId, parent?.Status.ToString() ?? "missing");
            resolution.ReadyConversationId = null;
            yield break;
        }

        var agent = subAgents.First(a => a.ToolName == parent.ToolName);
        var summarizeStartedAt = Stopwatch.GetTimestamp();
        var resultJson = succeeded
            ? await agent.SummarizeAsync(new SubAgentCompletion(userId, childConversationId, parent), cancellationToken)
            : "The session ended without producing a result.";
        _logger.LogInformation(
            "[{ClassName}] Sub-agent {Tool} conversation {ChildConversationId} parent {ParentRequestId} succeeded={Succeeded} summaryChars={SummaryChars} ms={ElapsedMs}",
            nameof(GatedToolApprovalResolver), parent.ToolName, childConversationId, parentRequestId, succeeded, resultJson.Length,
            (long)Stopwatch.GetElapsedTime(summarizeStartedAt).TotalMilliseconds);

        yield return new AiChatSubAgentCompleted(
            parent.ConversationId, parent.Id, childConversationId, toolRegistry.Find(parent.ToolName)?.DisplayName ?? parent.ToolName, succeeded);

        await foreach (var chatEvent in closer.ResolveAsync(userId, parent, r =>
        {
            r.Status = succeeded ? ToolExecutionStatus.Executed : ToolExecutionStatus.Failed;
            r.ResultJson = resultJson;
        }, resolution, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    /// <summary>The user abandoned a running sub-agent: deny whatever it was waiting on and settle the parent call as declined.</summary>
    public async IAsyncEnumerable<AiChatEvent> CancelSubAgentAsync(
        Guid userId, ToolExecutionRequest parent, ToolApprovalResolution resolution, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string reason = "The user cancelled this before it finished.";

        var child = await conversations.FindChildAsync(userId, parent.Id, cancellationToken);
        _logger.LogInformation("[{ClassName}] Cancelling sub-agent request {ParentRequestId} ({ToolName}), child conversation {ChildConversationId}",
            nameof(GatedToolApprovalResolver), parent.Id, parent.ToolName, child?.Id);
        if (child is not null)
        {
            await toolExecutions.DenyOutstandingForConversationAsync(child.Id, reason, cancellationToken);
        }

        yield return new AiChatSubAgentCompleted(
            parent.ConversationId, parent.Id, child?.Id ?? Guid.Empty, toolRegistry.Find(parent.ToolName)?.DisplayName ?? parent.ToolName, Success: false);

        await foreach (var chatEvent in closer.ResolveAsync(userId, parent, r =>
        {
            r.Status = ToolExecutionStatus.Denied;
            r.DenialReason = reason;
        }, resolution, cancellationToken))
        {
            yield return chatEvent;
        }
    }
}
