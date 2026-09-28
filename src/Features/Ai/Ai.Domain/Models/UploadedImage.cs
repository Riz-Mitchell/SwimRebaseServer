namespace Ai.Domain.Models;

/// <summary>
/// One image the user uploaded directly to blob storage via a presigned URL, for attaching to a chat
/// message. Chat messages reference these by <see cref="Id"/>, never by <see cref="ObjectKey"/> or a
/// storage URL directly - the object key is only ever resolved into a (short-lived, freshly generated)
/// presigned URL at the moment a message is actually sent to the model.
/// </summary>
public class UploadedImage
{
    public Guid Id { get; set; }

    /// <summary>FK to <see cref="AiUser"/>.</summary>
    public Guid UserId { get; set; }

    /// <summary>The blob storage key - stable forever, unlike any presigned URL generated from it.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>Set once <see cref="Status"/> becomes <see cref="UploadedImageStatus.Confirmed"/>.</summary>
    public long? SizeBytes { get; set; }

    public UploadedImageStatus Status { get; set; } = UploadedImageStatus.PendingUpload;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Set once this image has actually been attached to a sent message - blocks deletion from then on.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    public AiUser? AiUser { get; set; }
}
