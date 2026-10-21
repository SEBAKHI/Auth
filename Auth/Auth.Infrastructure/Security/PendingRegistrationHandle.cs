using Auth.Application.Interfaces;
using Auth.Domain.Enums;

namespace Auth.Infrastructure.Security;

/// <summary>
/// Keyed-digest implementation of <see cref="IPendingRegistrationHandle"/>.
/// </summary>
/// <remarks>
/// Fingerprinted by the credential hasher under its own purpose, and keeps its
/// legacy form apart from the other users of key 1 with a label no other caller
/// writes: <c>pending-registration-handle:v1:</c>. Rows store the handle
/// they were last issued a code under, and the start step re-keys it with every
/// code, so a rotation of the platform key changes what a NEW start derives
/// while a row's stored handle keeps matching until its next code: the handle
/// a client is answered with is always the stored one, never the derived one.
/// </remarks>
public sealed class PendingRegistrationHandle : IPendingRegistrationHandle
{
    /// <summary>
    /// Distinct from <c>otp:v1:</c> (codes) and <c>email:</c> (tombstones), so a
    /// handle can never be replayed as either.
    /// </summary>
    private const string Label = "pending-registration-handle:v1:";

    private readonly ICredentialHasher _credentialHasher;

    public PendingRegistrationHandle(ICredentialHasher credentialHasher)
    {
        _credentialHasher = credentialHasher;
    }

    /// <inheritdoc />
    public string For(string normalizedEmail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        return _credentialHasher.Hash(CredentialPurpose.PendingRegistrationHandle, Label + normalizedEmail);
    }
}
