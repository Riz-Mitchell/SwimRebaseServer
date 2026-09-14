using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

public sealed class ComparisonPointConfiguration : IEntityTypeConfiguration<ComparisonPoint>
{
    public void Configure(EntityTypeBuilder<ComparisonPoint> builder)
    {
        builder.ToTable("comparison_points");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Polarity).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Order).IsRequired();
        builder.Property(p => p.TagLine).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1000).IsRequired();

        builder.HasIndex(p => new { p.FocusId, p.Polarity, p.Order });
    }
}
