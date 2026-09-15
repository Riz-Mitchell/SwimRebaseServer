using Ai.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ai.Infrastructure.Configurations;

/// <summary>
/// The Ai schema's per-user anchor. Owns the real FK relationships to every other Ai entity, so
/// this is the one place those relationships are declared - the dependent-side configurations only
/// index their own FK column.
/// </summary>
public sealed class AiUserConfiguration : IEntityTypeConfiguration<AiUser>
{
    public void Configure(EntityTypeBuilder<AiUser> builder)
    {
        builder.ToTable("ai_users");
        builder.HasKey(u => u.Id);

        // Id is the Supabase 'sub' claim (same value as Auth's AppUser.Id) - never generated here.
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastActiveAt).IsRequired();

        builder.HasMany(u => u.Conversations)
            .WithOne(c => c.AiUser)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.ToolExecutionRequests)
            .WithOne(t => t.AiUser)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.TokenUsageRecords)
            .WithOne(t => t.AiUser)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.UploadedImages)
            .WithOne(i => i.AiUser)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
