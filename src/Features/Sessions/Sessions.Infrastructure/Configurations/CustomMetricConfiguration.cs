using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class CustomMetricConfiguration : IEntityTypeConfiguration<CustomMetric>
{
    public void Configure(EntityTypeBuilder<CustomMetric> builder)
    {
        builder.ToTable("custom_metrics");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).IsRequired().HasMaxLength(CustomMetricService.MaxNameLength);
        builder.Property(m => m.Strokes).IsRequired();
        builder.Property(m => m.Modes).IsRequired();
        builder.Property(m => m.Measure).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.Window).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.GroupBy).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(m => new { m.OwnerId, m.Position });
    }
}
