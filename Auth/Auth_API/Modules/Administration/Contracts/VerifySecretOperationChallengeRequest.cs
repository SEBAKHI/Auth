namespace Auth_API.Modules.Administration.Contracts;

/// <summary>
/// Request to answer a step-up confirmation with the emailed code.
/// </summary>
public record VerifySecretOperationChallengeRequest
{
    /// <summary>
    /// The six-digit code from the confirmation email.
    /// </summary>
    public string Code { get; init; } = string.Empty;
}
