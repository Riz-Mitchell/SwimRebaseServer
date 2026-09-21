using Ai.Application.Interfaces;
using Ai.Application.Models;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// The interviewer's final tool. Validates the session and stores it as a draft - nothing is saved to the user's log. Invalid input comes
/// back as a tool error listing what to fix (the interviewer just calls it again); a valid one ends the interview.
/// </summary>
public sealed class DraftSessionTool(SessionDraftService drafts) : IAiTool
{
    public string Name => InterviewTools.DraftSession;

    public string DisplayName => "Drafting your session";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description => InterviewTools.DraftSessionDescription;

    public string JsonSchema => InterviewTools.DraftSessionSchema;

    public bool RequiresApproval => false;

    public string? ScopedToAgent => InterviewTools.AgentKind;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        await drafts.CreateAsync(context.UserId, context.ConversationId, inputJson, cancellationToken);
        return new ToolExecutionResult("ok", AffectedCount: 1);
    }
}
