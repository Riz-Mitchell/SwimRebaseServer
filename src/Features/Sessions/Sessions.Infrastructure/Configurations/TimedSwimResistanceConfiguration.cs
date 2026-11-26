using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class TimedSwimResistanceConfiguration : IEntityTypeConfiguration<TimedSwimResistance>
{
    public void Configure(EntityTypeBuilder<TimedSwimResistance> builder)
    {
        builder.ToTable("timed_swim_resistances");
        builder.HasKey(r => r.Id);

        // Ids are assigned in code, so a row added through an already-tracked swim is inserted rather than assumed to exist.
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.TimedSwimId).IsRequired();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.WidthCm).HasPrecision(8, 2);
        builder.Property(r => r.HeightCm).HasPrecision(8, 2);
        builder.Property(r => r.LengthCm).HasPrecision(8, 2);
        builder.Property(r => r.WeightKg).HasPrecision(8, 2);
        builder.Property(r => r.Note).HasMaxLength(ResistanceCatalog.MaxNoteLength);

        builder.HasIndex(r => r.TimedSwimId);
        builder.HasIndex(r => r.Kind);
    }
}
