using Ai.Application.Interfaces;
using Ai.Application.Options;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application;
using Shared.Application.Storage;

namespace Ai.Application.Services;

/// <summary>
/// Orchestrates the two-step "upload directly to blob storage, then confirm" flow: the raw bytes
/// never pass through this API. <see cref="ConfirmUploadAsync"/> is what actually verifies something
/// was uploaded - a presigned PUT URL alone gives us no signal on its own.
/// </summary>
public sealed class ImageUploadService(
    IUploadedImageRepository images,
    IAiUserProvisioningService aiUserProvisioning,
    IBlobStorageService blobStorage,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<ImageUploadService> logger)
{
    public async Task<CreateUploadUrlResult> CreateUploadUrlAsync(
        Guid userId, string fileName, string contentType, CancellationToken cancellationToken)
    {
        await aiUserProvisioning.EnsureExistsAsync(userId, cancellationToken);

        var id = Guid.NewGuid();
        var extension = Path.GetExtension(fileName);
        var objectKey = $"chat-images/{userId}/{id}{extension}";

        var upload = blobStorage.CreatePresignedUploadUrl(objectKey, contentType, ImageUploadOptions.UploadUrlExpiry);

        images.Add(new UploadedImage
        {
            Id = id,
            UserId = userId,
            ObjectKey = objectKey,
            ContentType = contentType,
            Status = UploadedImageStatus.PendingUpload,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("[{ClassName}] Issued upload URL for image {ImageId} ({ContentType}) to user {UserId}", nameof(ImageUploadService), id, contentType, userId);

        return new CreateUploadUrlResult(id, upload.Url, upload.ExpiresAt);
    }

    public async Task<ConfirmUploadResult> ConfirmUploadAsync(Guid userId, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await images.FindAsync(userId, imageId, cancellationToken);
        if (image is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to confirm image {ImageId}, which does not exist for them", nameof(ImageUploadService), userId, imageId);
            return new ConfirmUploadResult(ConfirmUploadOutcome.NotFound, null);
        }

        if (image.Status == UploadedImageStatus.Confirmed)
        {
            logger.LogInformation("[{ClassName}] Image {ImageId} was already confirmed", nameof(ImageUploadService), imageId);
            return new ConfirmUploadResult(ConfirmUploadOutcome.Confirmed, image.SizeBytes);
        }

        var metadata = await blobStorage.TryGetMetadataAsync(image.ObjectKey, cancellationToken);
        if (metadata is null)
        {
            logger.LogWarning("[{ClassName}] Image {ImageId} confirmed by user {UserId} before anything was uploaded", nameof(ImageUploadService), imageId, userId);
            return new ConfirmUploadResult(ConfirmUploadOutcome.UploadNotFound, null);
        }

        if (metadata.SizeBytes > ImageUploadOptions.MaxSizeBytes)
        {
            logger.LogWarning("[{ClassName}] Image {ImageId} from user {UserId} is {SizeBytes} bytes, over the {MaxBytes}-byte limit; deleting it",
                nameof(ImageUploadService), imageId, userId, metadata.SizeBytes, ImageUploadOptions.MaxSizeBytes);
            await blobStorage.DeleteAsync(image.ObjectKey, cancellationToken);
            return new ConfirmUploadResult(ConfirmUploadOutcome.TooLarge, metadata.SizeBytes);
        }

        image.Status = UploadedImageStatus.Confirmed;
        image.SizeBytes = metadata.SizeBytes;
        image.ConfirmedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("[{ClassName}] Confirmed image {ImageId} for user {UserId} ({SizeBytes} bytes)", nameof(ImageUploadService), imageId, userId, metadata.SizeBytes);

        return new ConfirmUploadResult(ConfirmUploadOutcome.Confirmed, metadata.SizeBytes);
    }

    public async Task<DeleteImageOutcome> DeleteImageAsync(Guid userId, Guid imageId, CancellationToken cancellationToken)
    {
        var image = await images.FindAsync(userId, imageId, cancellationToken);
        if (image is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to delete image {ImageId}, which does not exist for them", nameof(ImageUploadService), userId, imageId);
            return DeleteImageOutcome.NotFound;
        }

        if (image.UsedAt is not null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to delete image {ImageId}, which is already attached to a message", nameof(ImageUploadService), userId, imageId);
            return DeleteImageOutcome.AlreadyUsed;
        }

        await blobStorage.DeleteAsync(image.ObjectKey, cancellationToken);
        images.Remove(image);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("[{ClassName}] Deleted image {ImageId} for user {UserId}", nameof(ImageUploadService), imageId, userId);

        return DeleteImageOutcome.Deleted;
    }
}

public sealed record CreateUploadUrlResult(Guid ImageId, string UploadUrl, DateTimeOffset ExpiresAt);

public enum ConfirmUploadOutcome
{
    Confirmed,

    /// <summary>No UploadedImage row with this id, or it belongs to someone else.</summary>
    NotFound,

    /// <summary>The row exists but nothing has actually landed in blob storage at its object key yet.</summary>
    UploadNotFound,

    TooLarge,
}

public sealed record ConfirmUploadResult(ConfirmUploadOutcome Outcome, long? SizeBytes);

public enum DeleteImageOutcome
{
    Deleted,

    /// <summary>No UploadedImage row with this id, or it belongs to someone else.</summary>
    NotFound,

    /// <summary>Already attached to a sent message - deleting it would break that message's image forever.</summary>
    AlreadyUsed,
}
