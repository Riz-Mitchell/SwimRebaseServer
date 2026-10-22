using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services.ToolInteractions;

/// <summary>A question for the user. The tool itself decides what the client is shown (<see cref="IQuestionTool.CreateQuestionEvent"/>).</summary>
public sealed class QuestionInteraction(ILogger<QuestionInteraction>? logger = null) : IToolInteraction
{
    private readonly ILogger<QuestionInteraction> _logger = logger ?? NullLogger<QuestionInteraction>.Instance;

    public ToolInteractionKind Kind => ToolInteractionKind.Question;

    public Task<IReadOnlyList<AiChatEvent>> RaiseAsync(ToolRaiseContext context, CancellationToken cancellationToken)
    {
        var tool = context.Tool as IQuestionTool;
        if (tool is null)
        {
            _logger.LogError("[{ClassName}] Tool {ToolName} is marked as a question but does not implement {Interface}", nameof(QuestionInteraction), context.Call.ToolName, nameof(IQuestionTool));
            throw new InvalidOperationException($"Tool '{context.Call.ToolName}' is marked as a question but does not implement {nameof(IQuestionTool)}.");
        }

        if (tool.Rejection(context.Call.InputJson) is { } reason)
        {
            // Nothing to show the user: settle the call now so the turn loop hands the reason back to the model, which asks again.
            _logger.LogWarning("[{ClassName}] Rejected question from {ToolName} in conversation {ConversationId}: {Reason}", nameof(QuestionInteraction), context.Call.ToolName, context.ConversationId, reason);
            context.Request.Status = ToolExecutionStatus.Failed;
            context.Request.ResultJson = reason;
            context.Request.ResolvedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<IReadOnlyList<AiChatEvent>>([]);
        }

        _logger.LogInformation("[{ClassName}] Asking user {UserId} a question via {ToolName} (request {ToolRequestId})", nameof(QuestionInteraction), context.UserId, context.Call.ToolName, context.Request.Id);
        IReadOnlyList<AiChatEvent> events =
            [tool.CreateQuestionEvent(context.ConversationId, context.Request.Id, context.Call.InputJson)];
        return Task.FromResult(events);
    }
}
