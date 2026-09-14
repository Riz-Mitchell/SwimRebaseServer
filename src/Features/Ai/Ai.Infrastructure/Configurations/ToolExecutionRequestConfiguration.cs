using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

public sealed class ToolExecutionRequestConfiguration : IEntityTypeConfiguration<ToolExecutionRequest>
{
    public void Configure(EntityTypeBuilder<ToolExecutionRequest> builder)
    {
        builder.ToTable("tool_execution_requests");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.AnthropicToolUseId).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ToolName).HasMaxLength(200).IsRequired();
        // Plain text, not jsonb - see ConversationMessageConfiguration.RawContentJson for why:
        // jsonb reorders object keys on storage, which breaks polymorphic JSON deserialization on
        // types with a "$type" discriminator, and neither column is ever queried at the SQL level.
        builder.Property(t => t.InputJson).HasColumnType("text").IsRequired();
        builder.Property(t => t.ResultJson).HasColumnType("text");
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        // Scoped lookup by user (the guard's core query) and grouping siblings from one turn.
        builder.HasIndex(t => new { t.UserId, t.Status });
        builder.HasIndex(t => t.AssistantMessageId);
    }
}
