using Ai.Application.Interfaces;
using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AiDbContext"/> - no SaveChanges.</summary>
public sealed class UploadedImageRepository(AiDbContext db) : IUploadedImageRepository
{
    public void Add(UploadedImage image) => db.UploadedImages.Add(image);

    public void Remove(UploadedImage image) => db.UploadedImages.Remove(image);

    public async Task<UploadedImage?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.UploadedImages.FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId, cancellationToken);
}
