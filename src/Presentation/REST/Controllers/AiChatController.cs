using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Services;
using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace REST.Controllers;

/// <summary>
/// Streams <see cref="AiChatEvent"/>s to the client as Server-Sent Events. The current user is
/// always resolved from the validated JWT via <see cref="ICurrentUserAccessor"/> - it is never
/// accepted from the request body or route.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ai")]
public sealed class AiChatController(
    ChatOrchestrator orchestrator,
    ICurrentUserAccessor currentUser,
    IConversationRepository conversations,
    ITokenUsageRepository tokenUsage) : ControllerBase
{
    // Two templates because an optional route parameter that isn't the final segment (this one is
    // followed by "/messages") doesn't collapse correctly when omitted - ASP.NET Core won't match
    // "conversations/messages" against "conversations/{conversationId:guid?}/messages" on its own.
    [HttpPost("conversations/messages")]
    [HttpPost("conversations/{conversationId:guid}/messages")]
    public Task SendMessage(Guid? conversationId, [FromBody] SendMessageRequest request, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.SendUserMessageAsync(currentUser.UserId, conversationId, request.Message, cancellationToken),
            cancellationToken);

    [HttpPost("tool-requests/{toolExecutionRequestId:guid}/resolve")]
    public Task ResolveToolRequest(
        Guid toolExecutionRequestId, [FromBody] ResolveToolRequestBody body, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.ResolveToolApprovalAsync(currentUser.UserId, toolExecutionRequestId, body.Approve, body.DenialReason, cancellationToken),
            cancellationToken);

    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(
        [FromQuery] ConversationSortBy sortBy = ConversationSortBy.LastMessageAt,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var result = await conversations.ListForUserAsync(currentUser.UserId, sortBy, descending, cancellationToken);
        return Ok(result.Select(c => new { c.Id, c.Title, c.CreatedAt, c.LastMessageAt }));
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var summary = await tokenUsage.GetSummaryForUserAsync(currentUser.UserId, from, to, cancellationToken);
        return Ok(summary);
    }

    private async Task StreamEventsAsync(IAsyncEnumerable<AiChatEvent> events, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var chatEvent in events.WithCancellation(cancellationToken))
        {
            var json = JsonSerializer.Serialize<AiChatEvent>(chatEvent);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}

public sealed record SendMessageRequest(string Message);

public sealed record ResolveToolRequestBody(bool Approve, string? DenialReason);
