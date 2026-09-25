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
public sealed class SaveSessionTool(SessionPromotionService promotion) : IAiTool
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

        var session = await promotion.SaveAsync(context.UserId, draftId, input.PerformedOn, course, input.Title, cancellationToken);

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

    private sealed record Input(string? DraftId, DateOnly PerformedOn, string? Course, string? Title);
}
