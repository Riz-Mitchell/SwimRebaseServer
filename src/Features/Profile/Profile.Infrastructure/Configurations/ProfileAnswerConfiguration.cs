using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Profile.Domain.Models;

namespace Profile.Infrastructure.Configurations;

public sealed class ProfileAnswerConfiguration : IEntityTypeConfiguration<ProfileAnswer>
{
    public void Configure(EntityTypeBuilder<ProfileAnswer> builder)
    {
        builder.ToTable("profile_answers");
        builder.HasKey(a => new { a.UserId, a.QuestionKey });

        builder.Property(a => a.QuestionKey).HasMaxLength(50);
        builder.Property(a => a.AnswerValue).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AnsweredAt).IsRequired();
    }
}
