using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Ai.Infrastructure;

public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    public DbSet<ConversationSummary> ConversationSummaries => Set<ConversationSummary>();

    public DbSet<ToolExecutionRequest> ToolExecutionRequests => Set<ToolExecutionRequest>();

    public DbSet<TokenUsageRecord> TokenUsageRecords => Set<TokenUsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiDbContext).Assembly);
    }
}
