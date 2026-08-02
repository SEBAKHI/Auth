using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the devices a user has signed in from before.
/// </summary>
public interface IUserKnownDeviceRepository
{
    /// <summary>Finds one device by its signature, or null when unrecognised.</summary>
    Task<UserKnownDevice?> GetAsync(Guid userId, string deviceHash, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the user has any recognised device at all. Distinguishes a first
    /// sign-in — which must not raise an alert about itself — from a genuinely
    /// new device on an established account.
    /// </summary>
    Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// When this user was last told about a new device, across all of their
    /// devices. Backs the per-user alert floor: someone who clears site data
    /// every session presents a new signature every time, and without a floor
    /// that is one email per sign-in forever.
    /// </summary>
    Task<DateTime?> GetLastAlertAtAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a sighting, inserting the device or refreshing an existing row.
    /// Returns true only when a row was inserted — concurrent sign-ins from the
    /// same new device race here, and this is what decides which one is the
    /// discovery and which is a duplicate.
    /// </summary>
    Task<bool> UpsertAsync(UserKnownDevice device, CancellationToken cancellationToken);
}
