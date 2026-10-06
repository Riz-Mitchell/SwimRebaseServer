using Billing.Application.Interfaces;
using Profile.Application.Interfaces;

namespace Billing.Application.Services;

public sealed class ProfileTrialReader(IUserProfileRepository profiles) : ITrialReader
{
    public async Task<DateTimeOffset?> TrialEndsAtAsync(Guid userId, CancellationToken cancellationToken) =>
        (await profiles.FindAsync(userId, cancellationToken))?.ProTrialEndsAt;
}
