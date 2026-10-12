using Auth.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Auth.Infrastructure;

/// <summary>Per instance, and only needs to outlive an access token.</summary>
public sealed class DeletedUserList(IMemoryCache cache) : IDeletedUserList
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    public void Add(Guid userId) => cache.Set(Key(userId), true, Retention);

    public bool Contains(Guid userId) => cache.TryGetValue(Key(userId), out _);

    private static string Key(Guid userId) => $"deleted-user:{userId}";
}
