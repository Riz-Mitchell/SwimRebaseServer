using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Profile.Domain.Models;

namespace Profile.Infrastructure.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(p => p.Id);

        // Id is the Supabase 'sub' claim - never generated here.
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.CurrentStep).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.FirstSessionPath).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.RemindersEnabled).IsRequired();
        builder.Property(p => p.ReminderLeadMinutes).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        builder.HasMany(p => p.Answers)
            .WithOne(a => a.UserProfile)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
