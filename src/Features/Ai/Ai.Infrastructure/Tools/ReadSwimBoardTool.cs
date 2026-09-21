using Ai.Application.Interfaces;
using Ai.Application.Models;
using SessionConversion.Application.Interview;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// The main chat's door to the swim-board interview. Definition only - it never "executes": <see cref="Ai.Application.Services.ToolInteractions.SubAgentInteraction"/>
/// starts the interviewer in its own conversation, and its finished draft becomes this call's tool_result.
/// </summary>
public sealed class ReadSwimBoardTool : IAiToolDefinition
{
    public string Name => InterviewTools.ReadSwimBoard;

    public string DisplayName => "Reading the swim board";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description => InterviewTools.ReadSwimBoardDescription;

    public string JsonSchema => InterviewTools.ReadSwimBoardSchema;

    public bool RequiresApproval => true;

    public ToolInteractionKind Interaction => ToolInteractionKind.SubAgent;

    /// <summary>Only offered when there is an image in the conversation to read.</summary>
    public ToolRequirement Requires => ToolRequirement.ImageAttachment;
}
