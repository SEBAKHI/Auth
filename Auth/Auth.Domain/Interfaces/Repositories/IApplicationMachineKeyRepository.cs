using Auth.Domain.Entities;

namespace Auth.Domain.Interfaces.Repositories;

/// <summary>
/// The outcome of storing a new machine key.
/// </summary>
public enum MachineKeyCreateOutcome
{
    Created = 1,

    /// <summary>The application already holds the most active keys allowed; nothing was written.</summary>
    LimitReached = 2,

    /// <summary>The same public key (the same <c>kid</c>) is already registered; nothing was written.</summary>
    DuplicateKey = 3,
}

/// <summary>
/// An active machine key, with the code of its application, as the token endpoint needs it.
/// </summary>
/// <param name="Key">The key; never revoked.</param>
/// <param name="ApplicationCode">The application's code: the machine client's <c>client_id</c>.</param>
public sealed record MachineKeyCredential(ApplicationMachineKey Key, string ApplicationCode);

/// <summary>
/// Repository of applications' machine keys (OI-128).
/// </summary>
public interface IApplicationMachineKeyRepository
{
    /// <summary>Every key of one application, revoked ones included, newest first.</summary>
    Task<IReadOnlyList<ApplicationMachineKey>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken);

    /// <summary>
    /// The key <paramref name="id"/> when it belongs to <paramref name="applicationId"/>; null
    /// otherwise, so a key of another application is indistinguishable from no key.
    /// </summary>
    Task<ApplicationMachineKey?> GetAsync(Guid applicationId, Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="key"/> only while its application holds fewer than
    /// <paramref name="maxActiveKeys"/> unrevoked keys. The count and the insert are one
    /// statement, so two concurrent creations cannot both pass a count of two.
    /// </summary>
    Task<MachineKeyCreateOutcome> TryCreateAsync(
        ApplicationMachineKey key, int maxActiveKeys, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the revocation of <paramref name="key"/> only when the stored key is still active.
    /// </summary>
    /// <returns>False when another request revoked it first.</returns>
    Task<bool> TryRevokeAsync(ApplicationMachineKey key, CancellationToken cancellationToken);

    /// <summary>
    /// The unrevoked key whose identifier is <paramref name="kid"/>, when its application is
    /// active and not deleted; null otherwise.
    /// </summary>
    Task<MachineKeyCredential?> GetActiveByKidAsync(string kid, CancellationToken cancellationToken);

    /// <summary>Records that the key obtained a machine token now.</summary>
    Task RecordUsageAsync(Guid id, CancellationToken cancellationToken);
}
