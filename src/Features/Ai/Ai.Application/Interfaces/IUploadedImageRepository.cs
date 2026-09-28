using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IUploadedImageRepository
{
    void Add(UploadedImage image);

    void Remove(UploadedImage image);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the image belongs to someone else.</summary>
    Task<UploadedImage?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}
