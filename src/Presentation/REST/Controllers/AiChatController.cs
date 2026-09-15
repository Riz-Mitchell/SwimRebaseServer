using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
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
    IToolExecutionRepository toolExecutions,
    ITokenUsageRepository tokenUsage,
    ImageUploadService imageUpload) : ControllerBase
{
    // Two templates because an optional route parameter that isn't the final segment (this one is
    // followed by "/messages") doesn't collapse correctly when omitted - ASP.NET Core won't match
    // "conversations/messages" against "conversations/{conversationId:guid?}/messages" on its own.
    [HttpPost("conversations/messages")]
    [HttpPost("conversations/{conversationId:guid}/messages")]
    public Task SendMessage(Guid? conversationId, [FromBody] SendMessageRequest request, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.SendUserMessageAsync(currentUser.UserId, conversationId, request.Message, request.ImageIds, cancellationToken),
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

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        [FromQuery] int? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.FindAsync(currentUser.UserId, conversationId, cancellationToken);
        if (conversation is null) return NotFound();

        limit = Math.Clamp(limit, 1, 100);

        var page = await conversations.GetMessagesPageAsync(conversationId, before, limit, cancellationToken);
        var hasMore = page.Count > limit;
        var items = hasMore ? page.Take(limit).ToList() : page;

        return Ok(new
        {
            items = items.Select(m => new { m.Id, m.SequenceNumber, m.Role, m.Content, m.CreatedAt }),
            hasMore,
            nextBefore = hasMore ? items[^1].SequenceNumber : (int?)null,
        });
    }

    [HttpGet("conversations/{conversationId:guid}/tool-requests/pending")]
    public async Task<IActionResult> GetPendingToolRequests(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(currentUser.UserId, conversationId, cancellationToken);
        if (conversation is null) return NotFound();

        var pending = await toolExecutions.GetPendingForConversationAsync(currentUser.UserId, conversationId, cancellationToken);
        return Ok(pending.Select(t => new
        {
            t.Id,
            t.AssistantMessageId,
            t.ToolName,
            t.InputJson,
            t.CreatedAt,
        }));
    }

    [HttpPost("images/upload-url")]
    public async Task<IActionResult> CreateImageUploadUrl([FromBody] CreateImageUploadUrlRequest request, CancellationToken cancellationToken)
    {
        if (!ImageUploadOptions.AllowedContentTypes.Contains(request.ContentType))
        {
            return BadRequest($"Unsupported content type '{request.ContentType}'. Allowed: {string.Join(", ", ImageUploadOptions.AllowedContentTypes)}.");
        }

        var result = await imageUpload.CreateUploadUrlAsync(currentUser.UserId, request.FileName, request.ContentType, cancellationToken);
        return Ok(new { imageId = result.ImageId, uploadUrl = result.UploadUrl, expiresAt = result.ExpiresAt });
    }

    [HttpPost("images/{imageId:guid}/confirm")]
    public async Task<IActionResult> ConfirmImageUpload(Guid imageId, CancellationToken cancellationToken)
    {
        var result = await imageUpload.ConfirmUploadAsync(currentUser.UserId, imageId, cancellationToken);
        return result.Outcome switch
        {
            ConfirmUploadOutcome.Confirmed => Ok(new { imageId, status = "Confirmed", sizeBytes = result.SizeBytes }),
            ConfirmUploadOutcome.NotFound => NotFound(),
            ConfirmUploadOutcome.UploadNotFound => Conflict("Nothing has been uploaded to this image's URL yet."),
            ConfirmUploadOutcome.TooLarge =>
                BadRequest($"Image is {result.SizeBytes} bytes, which exceeds the {ImageUploadOptions.MaxSizeBytes}-byte limit."),
            _ => StatusCode(500),
        };
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

public sealed record SendMessageRequest(string Message, IReadOnlyList<Guid>? ImageIds = null);

public sealed record ResolveToolRequestBody(bool Approve, string? DenialReason);

public sealed record CreateImageUploadUrlRequest(string FileName, string ContentType);
