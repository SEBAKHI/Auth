using Auth.Domain.Enums;
using Auth.Domain.Errors;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>
/// The step-up binding of the HMAC keyring (S11): what the challenge handlers did inline before
/// the signing keyring (S12) gave the step-up a second ring. The digests are unchanged.
/// </summary>
public sealed class HmacKeyringApproval : IKeyringApproval
{
    private readonly HmacKeyringChangeService _keyring;
    private readonly TimeProvider _timeProvider;

    public HmacKeyringApproval(HmacKeyringChangeService keyring, TimeProvider timeProvider)
    {
        _keyring = keyring;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SecretOperation> Operations { get; } =
        [SecretOperation.GenerateHmacKey, SecretOperation.ImportHmacKey];

    /// <inheritdoc />
    public async Task<ErrorOr<string>> DigestAsync(
        KeyringAction action, KeyringTarget target, string? value, CancellationToken cancellationToken)
    {
        var ring = await _keyring.ReadFileRingAsync(cancellationToken);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        var possible = _keyring.Allows(action, ring.Value, target.KeyId, _timeProvider.GetUtcNow().UtcDateTime);
        if (possible.IsError)
        {
            return possible.Errors;
        }

        return KeyringStepUp.Digest(action, KeyringStepUp.TargetOf(action, ring.Value, target.KeyId), value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// For a retire, what the change stops is what was issued while the key still issued, or
    /// nothing once its window has closed (DateTime.MinValue counts nothing).
    /// </remarks>
    public async Task<ErrorOr<KeyringImpactScope>> BindAsync(
        KeyringAction action, KeyringTarget target, string? value, string? payloadHash, CancellationToken cancellationToken)
    {
        var ring = await _keyring.ReadFileRingAsync(cancellationToken);
        if (ring.IsError)
        {
            return ring.Errors;
        }

        var digest = KeyringStepUp.Digest(action, KeyringStepUp.TargetOf(action, ring.Value, target.KeyId), value);
        if (!string.Equals(digest, payloadHash, StringComparison.Ordinal))
        {
            return SecretErrors.ChallengeNotApproved;
        }

        if (action != KeyringAction.Retire || target.KeyId is not { } keyId || ring.Value.Find(keyId) is not { } key)
        {
            return new KeyringImpactScope(IssuedBefore: null);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return new KeyringImpactScope(
            ring.Value.BoundedVerifyUntil(key) is { } until && until >= now
                ? key.DemotedAtUtc ?? now
                : DateTime.MinValue);
    }
}
