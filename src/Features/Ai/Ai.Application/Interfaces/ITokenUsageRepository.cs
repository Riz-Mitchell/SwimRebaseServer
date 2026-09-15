using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ITokenUsageRepository
{
    void Add(TokenUsageRecord record);

    /// <summary>Aggregated totals for one user, for cost-analysis / usage-limit surfaces.</summary>
    Task<TokenUsageSummary> GetSummaryForUserAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

public sealed record TokenUsageSummary(
    long InputTokens,
    long OutputTokens,
    long CacheCreationInputTokens,
    long CacheReadInputTokens,
    int TurnCount);
