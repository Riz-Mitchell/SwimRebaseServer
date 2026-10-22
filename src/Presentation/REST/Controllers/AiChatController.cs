using System.Text.Json;
using System.Text.Json.Serialization;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Application.Options;
using Ai.Application.Services;
using Ai.Domain.Models;
using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shared.Application.Storage;

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
    IAiToolRegistry toolRegistry,
    ITokenUsageRepository tokenUsage,
    ImageUploadService imageUpload,
    IBlobStorageService blobStorage,
    ConversationManagementService conversationManagement,
    ILogger<AiChatController> logger) : ControllerBase
{
    // Two templates because an optional route parameter that isn't the final segment (this one is
    // followed by "/messages") doesn't collapse correctly when omitted - ASP.NET Core won't match
    // "conversations/messages" against "conversations/{conversationId:guid?}/messages" on its own.
    [HttpPost("conversations/messages")]
    [HttpPost("conversations/{conversationId:guid}/messages")]
    public Task SendMessage(Guid? conversationId, [FromBody] SendMessageRequest request, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.SendUserMessageAsync(currentUser.UserId, conversationId, request.Message, request.ImageIds, cancellationToken),
            nameof(SendMessage),
            cancellationToken);

    [HttpPost("tool-requests/{toolExecutionRequestId:guid}/resolve")]
    public Task ResolveToolRequest(
        Guid toolExecutionRequestId, [FromBody] ResolveToolRequestBody body, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.ResolveToolApprovalAsync(currentUser.UserId, toolExecutionRequestId, body.Approve, body.DenialReason, cancellationToken),
            nameof(ResolveToolRequest),
            cancellationToken);

    [HttpPost("tool-requests/{toolExecutionRequestId:guid}/answer")]
    public Task AnswerToolRequest(
        Guid toolExecutionRequestId, [FromBody] AnswerToolRequestBody body, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.AnswerQuestionAsync(currentUser.UserId, toolExecutionRequestId, body.ToAnswers(), cancellationToken),
            nameof(AnswerToolRequest),
            cancellationToken);

    /// <summary>Abandons a running sub-agent (its tool call is InProgress), unblocking the chat. Whatever it was waiting on is denied.</summary>
    [HttpPost("tool-requests/{toolExecutionRequestId:guid}/cancel")]
    public Task CancelToolRequest(Guid toolExecutionRequestId, CancellationToken cancellationToken) =>
        StreamEventsAsync(
            orchestrator.CancelSubAgentAsync(currentUser.UserId, toolExecutionRequestId, cancellationToken),
            nameof(CancelToolRequest),
            cancellationToken);

    [HttpGet("conversations")]
    public async Task<IActionResult> ListConversations(
        [FromQuery] ConversationSortBy sortBy = ConversationSortBy.LastMessageAt,
        [FromQuery] bool descending = true,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var result = await conversations.ListForUserAsync(currentUser.UserId, sortBy, descending, includeArchived, cancellationToken);
        logger.LogDebug("[{ClassName}] Listed {Count} conversation(s) for user {UserId} (archived included: {IncludeArchived})", nameof(AiChatController), result.Count, currentUser.UserId, includeArchived);
        return Ok(result.Select(c => new { c.Id, c.Title, c.CreatedAt, c.LastMessageAt, c.IsArchived }));
    }

    [HttpPatch("conversations/{conversationId:guid}")]
    public async Task<IActionResult> RenameConversation(
        Guid conversationId, [FromBody] RenameConversationRequest request, CancellationToken cancellationToken)
    {
        var result = await conversationManagement.RenameAsync(currentUser.UserId, conversationId, request.Title, cancellationToken);
        return result switch
        {
            ConversationManagementResult.NotFound => NotFound(),
            ConversationManagementResult.InvalidTitle => BadRequest("Title cannot be empty."),
            _ => NoContent(),
        };
    }

    /// <summary>Archives the conversation and its whole sub-agent tree, and auto-denies any tool requests still awaiting approval anywhere in it.</summary>
    [HttpPost("conversations/{conversationId:guid}/archive")]
    public async Task<IActionResult> ArchiveConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await conversationManagement.ArchiveAsync(currentUser.UserId, conversationId, cancellationToken);
        return result == ConversationManagementResult.NotFound ? NotFound() : NoContent();
    }

    [HttpPost("conversations/{conversationId:guid}/unarchive")]
    public async Task<IActionResult> UnarchiveConversation(Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await conversationManagement.UnarchiveAsync(currentUser.UserId, conversationId, cancellationToken);
        return result == ConversationManagementResult.NotFound ? NotFound() : NoContent();
    }

    [HttpGet("conversations/{conversationId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        [FromQuery] int? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var conversation = await conversations.FindAsync(currentUser.UserId, conversationId, cancellationToken);
        if (conversation is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} asked for messages of conversation {ConversationId}, which does not exist for them", nameof(AiChatController), currentUser.UserId, conversationId);
            return NotFound();
        }

        limit = Math.Clamp(limit, 1, 100);

        var page = await conversations.GetMessagesPageAsync(conversationId, before, limit, cancellationToken);
        var hasMore = page.Count > limit;
        var items = hasMore ? page.Take(limit).ToList() : page;
        logger.LogDebug("[{ClassName}] Returned {Count} message(s) of conversation {ConversationId} (more available: {HasMore})", nameof(AiChatController), items.Count, conversationId, hasMore);

        return Ok(new
        {
            items = items.Select(m => new { m.Id, m.SequenceNumber, m.Role, m.Content, images = ImagesFor(m), m.CreatedAt }),
            hasMore,
            nextBefore = hasMore ? items[^1].SequenceNumber : (int?)null,
        });
    }

    private IReadOnlyList<object> ImagesFor(ConversationMessage message) =>
        ChatContentJson.Deserialize(message.RawContentJson)
            .OfType<ImageContentBlock>()
            .Select(b => (object)new
            {
                imageId = b.ImageId,
                contentType = b.ContentType,
                url = blobStorage.CreatePresignedDownloadUrl(b.ObjectKey, ImageUploadOptions.DownloadUrlExpiry),
            })
            .ToList();

    [HttpGet("conversations/{conversationId:guid}/tool-requests/pending")]
    public async Task<IActionResult> GetPendingToolRequests(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(currentUser.UserId, conversationId, cancellationToken);
        if (conversation is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} asked for pending tool requests of conversation {ConversationId}, which does not exist for them", nameof(AiChatController), currentUser.UserId, conversationId);
            return NotFound();
        }

        var pending = await toolExecutions.GetPendingForConversationAsync(currentUser.UserId, conversationId, cancellationToken);
        var result = new List<object>();
        foreach (var t in pending)
        {
            var preview = t.Status == ToolExecutionStatus.PendingApproval && toolRegistry.Find(t.ToolName) is IToolPreview previewer
                ? await previewer.PreviewAsync(currentUser.UserId, t.InputJson, cancellationToken)
                : null;
            result.Add(new
            {
                t.Id,
                t.ConversationId,
                t.Status,
                t.AssistantMessageId,
                t.ToolName,
                t.InputJson,
                preview,
                t.CreatedAt,
            });
        }

        logger.LogDebug("[{ClassName}] Returned {Count} pending tool request(s) for conversation {ConversationId}", nameof(AiChatController), result.Count, conversationId);
        return Ok(result);
    }

    [HttpPost("images/upload-url")]
    public async Task<IActionResult> CreateImageUploadUrl([FromBody] CreateImageUploadUrlRequest request, CancellationToken cancellationToken)
    {
        if (!ImageUploadOptions.AllowedContentTypes.Contains(request.ContentType))
        {
            logger.LogWarning("[{ClassName}] User {UserId} asked for an upload URL for unsupported content type '{ContentType}'", nameof(AiChatController), currentUser.UserId, request.ContentType);
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

    [HttpDelete("images/{imageId:guid}")]
    public async Task<IActionResult> DeleteImage(Guid imageId, CancellationToken cancellationToken)
    {
        var outcome = await imageUpload.DeleteImageAsync(currentUser.UserId, imageId, cancellationToken);
        return outcome switch
        {
            DeleteImageOutcome.Deleted => NoContent(),
            DeleteImageOutcome.NotFound => NotFound(),
            DeleteImageOutcome.AlreadyUsed => Conflict("This image has already been sent in a message and can no longer be deleted."),
            _ => StatusCode(500),
        };
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage(
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var summary = await tokenUsage.GetSummaryForUserAsync(currentUser.UserId, from, to, cancellationToken);
        logger.LogDebug("[{ClassName}] Returned token usage for user {UserId} between {From} and {To}", nameof(AiChatController), currentUser.UserId, from, to);
        return Ok(summary);
    }

    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task StreamEventsAsync(IAsyncEnumerable<AiChatEvent> events, string operation, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var count = 0;
        try
        {
            await foreach (var chatEvent in events.WithCancellation(cancellationToken))
            {
                var json = JsonSerializer.Serialize<AiChatEvent>(chatEvent, SseJsonOptions);
                await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                count++;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("[{ClassName}] {Operation} stream was cancelled by the client after {EventCount} event(s)", nameof(AiChatController), operation, count);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{ClassName}] {Operation} stream failed after {EventCount} event(s)", nameof(AiChatController), operation, count);
            throw;
        }

        logger.LogInformation("[{ClassName}] {Operation} stream finished after {EventCount} event(s)", nameof(AiChatController), operation, count);
    }
}

public sealed record SendMessageRequest(string Message, IReadOnlyList<Guid>? ImageIds = null);

public sealed record ResolveToolRequestBody(bool Approve, string? DenialReason);

/// <summary>One answer via <c>Answer</c> for a single question, or one per question (in the order asked) via <c>Answers</c> when several were asked together.</summary>
public sealed record AnswerToolRequestBody(string? Answer, IReadOnlyList<string>? Answers)
{
    public IReadOnlyList<string> ToAnswers() => Answers is { Count: > 0 } ? Answers : [Answer ?? string.Empty];
}

public sealed record CreateImageUploadUrlRequest(string FileName, string ContentType);

public sealed record RenameConversationRequest(string Title);
