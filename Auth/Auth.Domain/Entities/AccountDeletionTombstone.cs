using Auth.Domain.Primitives;

namespace Auth.Domain.Entities;

/// <summary>
/// Destruction registry entry written when an account is permanently destroyed:
/// {keyed identifier digest, deleted-at, policy version, key version} and
/// nothing else.
///
/// <para>
/// The digest is an HMAC of the address under a key this system retains, so the
/// row is pseudonymised personal data, not an anonymous record. It is therefore
/// kept only for the reservation window and then swept — deleting the row is
/// the erasure, because the key itself cannot be destroyed while any live
/// reservation still depends on it.
/// </para>
/// </summary>
public class AccountDeletionTombstone : EntityBase
{
    /// <summary>
    /// Gets the keyed HMAC-SHA256 digest of the account's normalized email.
    /// Unique: one tombstone per identifier, and the reservation key checked by
    /// every registration path.
    /// </summary>
    public string EmailHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the UTC instant of destruction. The reservation expires, and this
    /// row is swept, once the configured window has elapsed from here.
    /// </summary>
    public DateTime DeletedAtUtc { get; private set; }

    /// <summary>
    /// Gets the retention-policy version applied (format "YYYY.MM").
    /// </summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Gets which identifier HMAC key produced <see cref="EmailHash"/>. Without
    /// it the key could never be rotated: digests from an old key would be
    /// indistinguishable from current ones and every reservation would silently
    /// stop matching.
    /// </summary>
    public byte KeyVersion { get; private set; } = 1;

    private AccountDeletionTombstone() : base()
    {
    }

    public AccountDeletionTombstone(
        Guid id,
        string emailHash,
        DateTime deletedAtUtc,
        string policyVersion,
        byte keyVersion) : base(id)
    {
        EmailHash = emailHash;
        DeletedAtUtc = deletedAtUtc;
        PolicyVersion = policyVersion;
        KeyVersion = keyVersion;
    }

    /// <summary>
    /// Creates a tombstone for an account being destroyed now.
    /// </summary>
    /// <param name="emailHash">Keyed HMAC-SHA256 of the normalized email.</param>
    /// <param name="policyVersion">Retention-policy version in force.</param>
    /// <param name="keyVersion">Version of the identifier HMAC key in use.</param>
    public static AccountDeletionTombstone Create(
        string emailHash,
        string policyVersion,
        byte keyVersion)
    {
        return new AccountDeletionTombstone
        {
            EmailHash = emailHash,
            DeletedAtUtc = DateTime.UtcNow,
            PolicyVersion = policyVersion,
            KeyVersion = keyVersion
        };
    }
}
