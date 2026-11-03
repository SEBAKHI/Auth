using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// The step-up binding of the RSA signing keyring (S12): an approval is bound to
/// <c>keyring:rsa/{action}/{target}</c> and the imported key, from the ring the secrets file holds
/// (KeyringStepUp), so it is never spent on another action, another key, or the other ring.
/// </summary>
public sealed class RsaKeyringApproval : IKeyringApproval
{
    private readonly RsaKeyringChangeService _keyring;
    private readonly TimeProvider _timeProvider;

    public RsaKeyringApproval(RsaKeyringChangeService keyring, TimeProvider timeProvider)
    {
        _keyring = keyring;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SecretOperation> Operations { get; } =
        [SecretOperation.GenerateRsaKey, SecretOperation.ImportRsaKey];

    /// <inheritdoc />
    public async Task<ErrorOr<string>> DigestAsync(
        KeyringAction action, KeyringTarget target, string? value, CancellationToken cancellationToken)
    {
        var ring = await _keyring.ReadFileRingAsync(cancellationToken);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        var possible = _keyring.Allows(action, ring.Value, target.Kid, value, _timeProvider.GetUtcNow().UtcDateTime);
        if (possible.IsError)
        {
            return possible.Errors;
        }

        return KeyringStepUp.SigningDigest(action, KeyringStepUp.SigningTargetOf(action, ring.Value, target.Kid, value), value);
    }

    /// <inheritdoc />
    /// <remarks>A retire stops what the key still verifies: nothing once its window has closed, or for a compromised record.</remarks>
    public async Task<ErrorOr<KeyringImpactScope>> BindAsync(
        KeyringAction action, KeyringTarget target, string? value, string? payloadHash, CancellationToken cancellationToken)
    {
        var ring = await _keyring.ReadFileRingAsync(cancellationToken);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        var digest = KeyringStepUp.SigningDigest(action, KeyringStepUp.SigningTargetOf(action, ring.Value, target.Kid, value), value);
        if (!string.Equals(digest, payloadHash, StringComparison.Ordinal))
        {
            return SecretErrors.ChallengeNotApproved;
        }

        var stillAccepted = action != KeyringAction.Retire
            || (ring.Value.Find(target.Kid) is { } key && ring.Value.Verifies(key, _timeProvider.GetUtcNow().UtcDateTime));
        return new KeyringImpactScope(IssuedBefore: null, stillAccepted);
    }
}
