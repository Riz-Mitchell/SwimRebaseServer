using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

public sealed class FocusCheckInConfiguration : IEntityTypeConfiguration<FocusCheckIn>
{
    public void Configure(EntityTypeBuilder<FocusCheckIn> builder)
    {
        builder.ToTable("focus_check_ins");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Date).IsRequired();
        builder.Property(c => c.CheckedInAt).IsRequired();

        // A focus can complete at most one day per calendar date.
        builder.HasIndex(c => new { c.FocusId, c.Date }).IsUnique();
    }
}
