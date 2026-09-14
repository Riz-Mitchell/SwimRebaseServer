using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

public sealed class FocusConfiguration : IEntityTypeConfiguration<Focus>
{
    public void Configure(EntityTypeBuilder<Focus> builder)
    {
        builder.ToTable("focuses");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.TagLine).HasMaxLength(200).IsRequired();
        builder.Property(f => f.Prompt).HasMaxLength(2000).IsRequired();
        builder.Property(f => f.OriginalUserRequest).HasMaxLength(2000);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(f => f.Origin).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(f => f.StartedAt).IsRequired();

        builder.Ignore(f => f.PositivePoints);
        builder.Ignore(f => f.NegativePoints);

        // Primary access pattern: "this user's active focuses, in priority order". Filtered to
        // Active only - completed/abandoned focuses keep their last priority value and would
        // otherwise collide with each other or with a currently active one.
        builder.HasIndex(f => new { f.UserId, f.Priority }).IsUnique().HasFilter("status = 'Active'");

        builder.HasMany(f => f.ComparisonPoints)
            .WithOne(p => p.Focus)
            .HasForeignKey(p => p.FocusId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.CheckIns)
            .WithOne(c => c.Focus)
            .HasForeignKey(c => c.FocusId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
