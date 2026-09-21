using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>A question for the user. The tool itself decides what the client is shown (<see cref="IQuestionTool.CreateQuestionEvent"/>).</summary>
public sealed class QuestionInteraction : IToolInteraction
{
    public ToolInteractionKind Kind => ToolInteractionKind.Question;

    public Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var tool = context.Tool as IQuestionTool
            ?? throw new InvalidOperationException($"Tool '{context.Call.ToolName}' is marked as a question but does not implement {nameof(IQuestionTool)}.");

        IReadOnlyList<AiChatEvent> events =
            [tool.CreateQuestionEvent(context.ConversationId, context.Request.Id, context.Call.InputJson)];
        return Task.FromResult(events);
    }
}
