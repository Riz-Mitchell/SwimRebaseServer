using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure.Configurations;

public sealed class UserEffortTermConfiguration : IEntityTypeConfiguration<UserEffortTerm>
{
    public void Configure(EntityTypeBuilder<UserEffortTerm> builder)
    {
        builder.ToTable("user_effort_terms");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Phrase).IsRequired().HasMaxLength(UserEffortTermService.MaxPhraseLength);
        builder.Property(t => t.Percent).IsRequired();
        builder.HasIndex(t => new { t.OwnerId, t.Phrase }).IsUnique();
    }
}
