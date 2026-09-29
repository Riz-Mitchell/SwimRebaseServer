using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>A question for the user. The tool itself decides what the client is shown (<see cref="IQuestionTool.CreateQuestionEvent"/>).</summary>
public sealed class QuestionInteraction : IToolInteraction
{
    public ToolInteractionKind Kind => ToolInteractionKind.Question;

    public Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var tool = context.Tool as IQuestionTool
            ?? throw new InvalidOperationException($"Tool '{context.Call.ToolName}' is marked as a question but does not implement {nameof(IQuestionTool)}.");

        if (tool.Rejection(context.Call.InputJson) is { } reason)
        {
            // Nothing to show the user: settle the call now so the turn loop hands the reason back to the model, which asks again.
            context.Request.Status = ToolExecutionStatus.Failed;
            context.Request.ResultJson = reason;
            context.Request.ResolvedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<IReadOnlyList<AiChatEvent>>([]);
        }

        IReadOnlyList<AiChatEvent> events =
            [tool.CreateQuestionEvent(context.ConversationId, context.Request.Id, context.Call.InputJson)];
        return Task.FromResult(events);
    }
}
