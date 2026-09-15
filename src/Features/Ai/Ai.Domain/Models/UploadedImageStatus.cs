namespace Ai.Domain.Models;

public enum UploadedImageStatus
{
    /// <summary>A presigned upload URL was issued but the client hasn't confirmed the upload finished yet.</summary>
    PendingUpload = 0,

    /// <summary>The object was verified to actually exist in storage - safe to reference in a chat message.</summary>
    Confirmed = 1,
}
