using System.Collections.Frozen;
using Auth.Domain.Enums;
using ErrorOr;

namespace Auth.Application.Features.Secrets.Common;

/// <summary>The key a keyring change names: an HMAC key by id, a signing key by kid.</summary>
/// <param name="KeyId">The HMAC key a reinstate or a retire names.</param>
/// <param name="Kid">The signing key a retire names.</param>
public sealed record KeyringTarget(int? KeyId, string? Kid);

/// <summary>Which credentials a keyring change can stop, for its cost (S11, S12).</summary>
/// <param name="IssuedBefore">Count only what was issued before this instant; null counts everything live.</param>
/// <param name="KeyStillAccepted">For a retire, whether the key is still accepted now; a key past its window stops nothing.</param>
public sealed record KeyringImpactScope(DateTime? IssuedBefore, bool KeyStillAccepted = true);

/// <summary>
/// The step-up binding of one keyring (KR3): the digest an approval for one of its changes is
/// bound to, from the ring the secrets file holds. One implementation per ring, chosen by the
/// operation that confirms it (<see cref="Operations"/>), never by a branch on the ring.
/// </summary>
public interface IKeyringApproval
{
    /// <summary>The step-up operations that confirm this ring's changes.</summary>
    IReadOnlyCollection<SecretOperation> Operations { get; }

    /// <summary>
    /// The digest an approval for <paramref name="action"/> is bound to, refusing first whatever
    /// would refuse the change, so no code is emailed for a change that cannot happen.
    /// </summary>
    Task<ErrorOr<string>> DigestAsync(
        KeyringAction action, KeyringTarget target, string? value, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that the change the administrator claims is the one the confirmation was raised for
    /// (<paramref name="payloadHash"/>), and answers what it can stop.
    /// </summary>
    Task<ErrorOr<KeyringImpactScope>> BindAsync(
        KeyringAction action, KeyringTarget target, string? value, string? payloadHash, CancellationToken cancellationToken);
}

/// <summary>The keyring approval bindings, by the operation that confirms each ring's changes.</summary>
public sealed class KeyringApprovals
{
    private readonly FrozenDictionary<SecretOperation, IKeyringApproval> _byOperation;

    public KeyringApprovals(IEnumerable<IKeyringApproval> approvals)
    {
        _byOperation = approvals
            .SelectMany(approval => approval.Operations.Select(operation => (operation, approval)))
            .ToFrozenDictionary(pair => pair.operation, pair => pair.approval);
    }

    /// <summary>The binding of the ring <paramref name="operation"/> confirms changes of; null for an operation of no keyring.</summary>
    public IKeyringApproval? For(SecretOperation operation) =>
        _byOperation.TryGetValue(operation, out var approval) ? approval : null;
}
