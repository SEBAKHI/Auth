namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request body for a destructive secret operation that carries no key material
/// of its own — the three regenerate endpoints. The confirmation is mandatory:
/// there is no unconfirmed path to rotating a key.
/// </summary>
public record ConfirmedSecretOperationRequest
{
    /// <summary>
    /// The verified step-up confirmation authorizing this operation. Spent on
    /// use, so a second request with the same id is rejected.
    /// </summary>
    public Guid ChallengeId { get; init; }
}
