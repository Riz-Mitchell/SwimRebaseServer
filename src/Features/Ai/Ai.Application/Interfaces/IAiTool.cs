using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <summary>How a tool call is handled once the model requests it. Drives which <see cref="IToolInteraction"/> raises it.</summary>
public enum ToolInteractionKind
{
    /// <summary>Executes immediately, no human involved.</summary>
    Auto,

    /// <summary>Waits for the user to approve or deny, then executes.</summary>
    Approval,

    /// <summary>Waits for the user to answer, and the answer becomes the tool_result.</summary>
    Question,

    /// <summary>Starts a sub-agent's own conversation; its outcome becomes the tool_result.</summary>
    SubAgent,
}

/// <summary>What a tool needs from the conversation to do its job. It is not a condition for offering the tool: the tool list is part of the cached prompt prefix and stays the same on every turn, so a tool called without what it needs fails with a message the model can act on.</summary>
public enum ToolRequirement
{
    None,

    /// <summary>Reads an image attached to the conversation. Once it has answered, the image is replaced by a placeholder in the model's context.</summary>
    ImageAttachment,
}

/// <summary>What the model needs to see to call a tool. Says nothing about how the call is carried out - see <see cref="IAiTool"/> for tools that execute.</summary>
public interface IAiToolDefinition
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
    /// True for any call that must wait on a human before the model can continue - a write/destructive
    /// action, a question, or a sub-agent. Such calls are never auto-executed; they are persisted as a
    /// <c>ToolExecutionRequest</c>.
    /// </summary>
    bool RequiresApproval { get; }

    /// <summary>Defaults to <see cref="ToolInteractionKind.Approval"/> / <see cref="ToolInteractionKind.Auto"/> from <see cref="RequiresApproval"/>; questions and sub-agents override it.</summary>
    ToolInteractionKind Interaction => RequiresApproval ? ToolInteractionKind.Approval : ToolInteractionKind.Auto;

    /// <summary>
    /// The <see cref="AgentProfile.Kind"/> this tool belongs to exclusively, or null for a shared tool that
    /// every profile marked <see cref="AgentProfile.IncludesSharedTools"/> (the main chat) offers.
    /// </summary>
    string? ScopedToAgent => null;

    ToolRequirement Requires => ToolRequirement.None;
}

/// <summary>
/// A tool the model can call that is carried out by code. Implementations live in Ai.Infrastructure and
/// register themselves via DI - the orchestration and approval pipeline never changes to accommodate a new one.
/// </summary>
public interface IAiTool : IAiToolDefinition
{
    /// <summary>
    /// Executes the tool. Implementations MUST scope every data access to <see cref="ToolExecutionContext.UserId"/> -
    /// never trust an id embedded in <paramref name="inputJson"/>.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken);
}

/// <summary>Who is calling a tool and from where. <see cref="ConversationId"/> is the conversation the call was made in (a sub-agent's own conversation, when it is one).</summary>
public sealed record ToolExecutionContext(Guid UserId, Guid ConversationId, string ToolUseId);

/// <summary>A <see cref="ToolInteractionKind.Question"/> tool: knows how to present its input to the user and how to turn their answers into the tool_result.</summary>
public interface IQuestionTool : IAiToolDefinition
{
    /// <summary>The event that asks the user this call's question(s).</summary>
    AiChatEvent CreateQuestionEvent(Guid conversationId, Guid toolExecutionRequestId, string inputJson);

    /// <summary>Builds the tool_result text from the user's answers (one per question actually asked).</summary>
    string FormatAnswer(string inputJson, IReadOnlyList<string> answers, QuestionAnswerContext context);
}

/// <param name="QuestionTurn">1-based count of this tool's calls in the conversation, including the one being answered.</param>
/// <param name="MaxQuestionTurns">The profile's cap on question turns, if it has one.</param>
public sealed record QuestionAnswerContext(int QuestionTurn, int? MaxQuestionTurns)
{
    public bool IsLastChance => MaxQuestionTurns is { } max && QuestionTurn >= max;
}
