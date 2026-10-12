using Microsoft.EntityFrameworkCore;
using Shared.Application;
using Shared.Application.Storage;

namespace Ai.Infrastructure;

/// <summary>Blobs first: the rows hold the storage keys, so a failed blob delete leaves them to retry. The cascade removes everything else.</summary>
public sealed class AiUserDataEraser(AiDbContext db, IBlobStorageService blobStorage) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var objectKeys = await db.UploadedImages
            .Where(i => i.UserId == userId)
            .Select(i => i.ObjectKey)
            .ToListAsync(cancellationToken);

        foreach (var objectKey in objectKeys)
        {
            await blobStorage.DeleteAsync(objectKey, cancellationToken);
        }

        await db.AiUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
