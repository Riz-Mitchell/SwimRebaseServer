using Ai.Application.Models;

namespace Ai.Application.Interfaces;

/// <param name="Title">Null when no usable title came back; the conversation keeps the one it was created with.</param>
/// <param name="Usage">The model call's usage, to be recorded by the caller; null when no call completed.</param>
public sealed record TitleResult(string? Title, ChatTokenUsage? Usage)
{
    public static TitleResult None { get; } = new(null, null);
}

/// <summary>
/// Names a new chat from its first message. Runs alongside the first reply, so it must not touch the database (the scoped context is not
/// thread-safe): the caller saves the title and records the usage. It never throws - a failure is <see cref="TitleResult.None"/>.
/// </summary>
public interface IConversationTitler
{
    Task<TitleResult> GenerateAsync(string firstMessage, bool hasImages, CancellationToken cancellationToken);
}
