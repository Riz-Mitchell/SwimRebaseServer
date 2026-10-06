using Microsoft.EntityFrameworkCore;
using Profile.Application.Interfaces;
using Profile.Domain.Models;

namespace Profile.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="ProfileDbContext"/> - no SaveChanges.</summary>
public sealed class UserProfileRepository(ProfileDbContext db) : IUserProfileRepository
{
    private const int DefaultReminderLeadMinutes = 60;

    public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.UserProfiles.Include(p => p.Answers).SingleOrDefaultAsync(p => p.Id == userId, cancellationToken);

    public async Task EnsureExistsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var step = nameof(OnboardingStep.Questionnaire);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO user_profiles (id, current_step, reminders_enabled, reminder_lead_minutes, created_at, updated_at)
            VALUES ({userId}, {step}, false, {DefaultReminderLeadMinutes}, {now}, {now})
            ON CONFLICT (id) DO NOTHING
            """,
            cancellationToken);
    }

    public void AddAnswer(ProfileAnswer answer) => db.ProfileAnswers.Add(answer);

    public void RemoveAnswer(ProfileAnswer answer) => db.ProfileAnswers.Remove(answer);
}
