using Analytics.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Analytics.Infrastructure.Configurations;

public sealed class EventLogConfiguration : IEntityTypeConfiguration<EventLog>
{
    public void Configure(EntityTypeBuilder<EventLog> builder)
    {
        builder.ToTable("event_logs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();

        builder.HasIndex(e => new { e.UserId, e.OccurredAt });
        builder.HasIndex(e => new { e.EventType, e.OccurredAt });
        builder.HasIndex(e => e.OccurredAt);
    }
}
