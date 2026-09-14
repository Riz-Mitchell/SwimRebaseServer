using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Training.Domain.Models;

namespace Training.Infrastructure.Configurations;

/// <summary>
/// The Training schema's per-user anchor. Owns the real FK relationships to every other Training
/// entity, so this is the one place those relationships are declared.
/// </summary>
public sealed class TrainingUserConfiguration : IEntityTypeConfiguration<TrainingUser>
{
    public void Configure(EntityTypeBuilder<TrainingUser> builder)
    {
        builder.ToTable("training_users");
        builder.HasKey(u => u.Id);

        // Id is the Supabase 'sub' claim (same value as Auth's AppUser.Id) - never generated here.
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.LastActiveAt).IsRequired();

        builder.HasMany(u => u.TimetableEntries)
            .WithOne(t => t.TrainingUser)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.Focuses)
            .WithOne(f => f.TrainingUser)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
