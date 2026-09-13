using Ai.Application.Interfaces;
using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="AiDbContext"/> - no SaveChanges.</summary>
public sealed class TokenUsageRepository(AiDbContext db) : ITokenUsageRepository
{
    public void Add(TokenUsageRecord record) => db.TokenUsageRecords.Add(record);

    public async Task<TokenUsageSummary> GetSummaryForUserAsync(
        Guid userId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var query = db.TokenUsageRecords.Where(t => t.UserId == userId);

        if (from is { } f)
        {
            query = query.Where(t => t.CreatedAt >= f);
        }

        if (to is { } until)
        {
            query = query.Where(t => t.CreatedAt <= until);
        }

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                InputTokens = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens),
                CacheCreationInputTokens = g.Sum(x => x.CacheCreationInputTokens),
                CacheReadInputTokens = g.Sum(x => x.CacheReadInputTokens),
                TurnCount = g.Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new TokenUsageSummary(0, 0, 0, 0, 0)
            : new TokenUsageSummary(
                totals.InputTokens, totals.OutputTokens, totals.CacheCreationInputTokens, totals.CacheReadInputTokens, totals.TurnCount);
    }
}
