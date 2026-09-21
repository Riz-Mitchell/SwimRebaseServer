using Ai.Application.Interfaces;
using Ai.Application.Models;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Lets the main chat change a draft the interview produced ("actually I skipped the kick set") with small path-addressed edits, so the
/// whole session never has to round-trip through the model. Edits only a draft, never saved data, so it needs no approval.
/// </summary>
public sealed class EditDraftTool(SessionDraftService drafts) : IAiTool
{
    public string Name => InterviewTools.EditDraft;

    public string DisplayName => "Updating the draft";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description => InterviewTools.EditDraftDescription;

    public string JsonSchema => InterviewTools.EditDraftSchema;

    public bool RequiresApproval => false;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        var request = DraftEditRequest.Parse(inputJson);
        var draft = await drafts.EditAsync(context.UserId, request.DraftId, request.Edits, cancellationToken);

        return new ToolExecutionResult($"Draft {draft.Id} is now version {draft.Version}.\n{draft.Outline}", AffectedCount: request.Edits.Count);
    }
}
