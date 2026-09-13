using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

public sealed class TokenUsageRecordConfiguration : IEntityTypeConfiguration<TokenUsageRecord>
{
    public void Configure(EntityTypeBuilder<TokenUsageRecord> builder)
    {
        builder.ToTable("token_usage_records");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Model).HasMaxLength(100).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        // Primary access pattern: per-user cost rollups over a time range.
        builder.HasIndex(t => new { t.UserId, t.CreatedAt });
    }
}
