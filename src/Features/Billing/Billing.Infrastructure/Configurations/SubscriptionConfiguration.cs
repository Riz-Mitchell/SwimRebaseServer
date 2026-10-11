using Billing.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billing.Infrastructure.Configurations;

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).ValueGeneratedNever();
        builder.Property(s => s.ProductId).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Store).HasMaxLength(40).IsRequired();
    }
}

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(200).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(60).IsRequired();
    }
}

public sealed class ConversionUsageConfiguration : IEntityTypeConfiguration<ConversionUsage>
{
    public void Configure(EntityTypeBuilder<ConversionUsage> builder)
    {
        builder.ToTable("conversion_usages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.HasIndex(u => new { u.UserId, u.CreatedAt });
        builder.HasIndex(u => u.ConversationId).IsUnique();
    }
}
