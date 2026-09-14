using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

public sealed class ExpectedSessionConfiguration : IEntityTypeConfiguration<ExpectedSession>
{
    public void Configure(EntityTypeBuilder<ExpectedSession> builder)
    {
        builder.ToTable("expected_sessions");

        // Shares its key with the owning TimetableEntry - a zero-or-one relationship needs no
        // separate id or FK column.
        builder.HasKey(s => s.TimetableEntryId);

        builder.Property(s => s.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(s => s.TotalDistanceMeters).IsRequired();
        builder.Property(s => s.PrimaryEffortZone).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}
