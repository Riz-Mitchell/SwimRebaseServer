using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Sessions.Domain.Models;
using SessionConversion.Application.Interview;
using SessionConversion.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Saves a draft from the swim-board import as one of the user's sessions. Always gated: it adds to their training log. Saving the
/// same draft twice returns the session the first save made, so a retried or repeated call is harmless.
/// </summary>
public sealed class SaveSessionTool(SessionPromotionService promotion, SessionDraftService drafts) : IAiTool, IToolPreview
{
    public string Name => InterviewTools.SaveSession;

    public string DisplayName => "Saving your session";

    public ToolEffect Effect => ToolEffect.Creative;

    public string Description => InterviewTools.SaveSessionDescription;

    public string JsonSchema => InterviewTools.SaveSessionSchema;

    public bool RequiresApproval => true;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        if (!Guid.TryParse(input.DraftId, out var draftId))
        {
            throw new InvalidOperationException("draftId is missing or not a valid id - use the draft id from the read_swim_board result.");
        }

        if (!Enum.TryParse<PoolCourse>(input.Course, ignoreCase: true, out var course))
        {
            throw new InvalidOperationException("course must be one of scy, scm, lcm.");
        }

        if (input.PerformedOn is not { } performedOn || performedOn == default)
        {
            throw new InvalidOperationException("performedOn is missing - ask the user which date they swam (yyyy-MM-dd).");
        }

        var session = await promotion.SaveAsync(context.UserId, draftId, performedOn, course, input.Title, cancellationToken);

        var resultJson = JsonSerializer.Serialize(
            new
            {
                sessionId = session.Id,
                performedOn = session.PerformedOn,
                title = session.Title,
                course = session.Course.ToString().ToLowerInvariant(),
                totalDistance = session.TotalDistance,
                outline = session.Outline,
            },
            ToolJsonOptions.Web);

        return new ToolExecutionResult(resultJson, AffectedCount: 1);
    }

    public async Task<string?> PreviewAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        try
        {
            var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web);
            if (input is null || !Guid.TryParse(input.DraftId, out var draftId))
            {
                return null;
            }

            return (await drafts.GetAsync(userId, draftId, cancellationToken)).Outline;
        }
        catch (Exception ex) when (ex is JsonException or DraftNotFoundException)
        {
            return null;
        }
    }

    private sealed record Input(string? DraftId, DateOnly? PerformedOn, string? Course, string? Title);
}
