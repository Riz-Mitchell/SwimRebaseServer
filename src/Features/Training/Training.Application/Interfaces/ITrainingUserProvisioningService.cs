namespace Training.Application.Interfaces;

/// <summary>
/// Ensures the caller has a <c>TrainingUser</c> anchor row before any Training data is written on
/// their behalf - the single point of contact that lets every other Training entity rely on a
/// real, database-enforced FK instead of a bare, unchecked user id.
/// </summary>
public interface ITrainingUserProvisioningService
{
    Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken);
}
