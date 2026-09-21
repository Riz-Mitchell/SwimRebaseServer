using SessionConversion.Domain.Models;

namespace SessionConversion.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>). Every read is scoped to a caller-supplied user id.</summary>
public interface ISessionDraftRepository
{
    void Add(SessionDraft draft);

    Task<SessionDraft?> FindAsync(Guid userId, Guid draftId, CancellationToken cancellationToken);

    /// <summary>The newest draft produced in the given conversation, if any.</summary>
    Task<SessionDraft?> FindLatestForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}
