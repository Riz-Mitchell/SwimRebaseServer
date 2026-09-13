using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

public sealed class ConversationSummaryConfiguration : IEntityTypeConfiguration<ConversationSummary>
{
    public void Configure(EntityTypeBuilder<ConversationSummary> builder)
    {
        builder.ToTable("conversation_summaries");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Content).IsRequired();
        builder.Property(s => s.CoveredThroughSequenceNumber).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired();

        // Backs "give me the latest summary for this conversation".
        builder.HasIndex(s => new { s.ConversationId, s.CoveredThroughSequenceNumber });
    }
}
