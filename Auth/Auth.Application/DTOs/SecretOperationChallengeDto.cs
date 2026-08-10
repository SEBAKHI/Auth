namespace Auth.Application.DTOs;

/// <summary>
/// Confirmation challenge raised against a destructive secret operation.
/// Carries no code and no unmasked address.
/// </summary>
public record SecretOperationChallengeDto
{
    /// <summary>
    /// The challenge to answer with the emailed code.
    /// </summary>
    public required Guid ChallengeId { get; init; }

    /// <summary>
    /// UTC instant after which the code stops being accepted.
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Masked address the code was sent to, so the administrator knows which
    /// mailbox to open without the response disclosing the full address.
    /// </summary>
    public required string MaskedEmail { get; init; }
}
