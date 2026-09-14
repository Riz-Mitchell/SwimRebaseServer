using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

public sealed class TimetableEntryConfiguration : IEntityTypeConfiguration<TimetableEntry>
{
    public void Configure(EntityTypeBuilder<TimetableEntry> builder)
    {
        builder.ToTable("timetable_entries");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.DayOfWeek).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();
        builder.Property(t => t.SessionType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Label).HasMaxLength(200);
        builder.Property(t => t.EffectiveFrom).IsRequired();

        // Primary access pattern: "every slot version active on date X for this user".
        builder.HasIndex(t => new { t.UserId, t.EffectiveFrom, t.EffectiveTo });

        // Fast lookup of a slot's currently-open version when creating its next one.
        builder.HasIndex(t => new { t.SlotId, t.EffectiveTo });

        builder.HasOne(t => t.ExpectedSession)
            .WithOne(s => s.TimetableEntry)
            .HasForeignKey<ExpectedSession>(s => s.TimetableEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
