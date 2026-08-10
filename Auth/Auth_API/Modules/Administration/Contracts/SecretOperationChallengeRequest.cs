using Auth.Domain.Enums;

namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to raise a step-up confirmation for a destructive secret operation.
/// </summary>
public record SecretOperationChallengeRequest
{
    /// <summary>
    /// The operation the resulting approval will authorize, and nothing else.
    /// </summary>
    public SecretOperation Operation { get; init; }

    /// <summary>
    /// The key material for the import operations, so the confirmation is bound
    /// to the exact bytes being approved. Omit for the generate operations.
    /// </summary>
    public string? Value { get; init; }
}
