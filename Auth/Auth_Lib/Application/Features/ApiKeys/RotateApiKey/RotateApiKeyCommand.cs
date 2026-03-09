using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.ApiKeys.RotateApiKey;

/// <summary>
/// Command to rotate an API key, generating a new key while optionally
/// keeping the old key valid for a grace period.
/// </summary>
/// <param name="ApiKeyId">The ID of the API key to rotate.</param>
/// <param name="GracePeriodMinutes">Time in minutes to keep the old key valid. Default is 60 minutes.</param>
/// <param name="RotatedBy">The ID of the user performing the rotation.</param>
public record RotateApiKeyCommand(
    Guid ApiKeyId,
    int GracePeriodMinutes,
    Guid RotatedBy
) : IRequest<ErrorOr<RotateApiKeyResponse>>;

/// <summary>
/// Response from API key rotation containing both old and new keys.
/// </summary>
public record RotateApiKeyResponse
{
    /// <summary>
    /// The new API key (only shown once - store securely).
    /// </summary>
    public required string NewApiKey { get; init; }

    /// <summary>
    /// The ID of the new API key.
    /// </summary>
    public Guid NewApiKeyId { get; init; }

    /// <summary>
    /// The prefix of the new API key for identification.
    /// </summary>
    public required string NewKeyPrefix { get; init; }

    /// <summary>
    /// When the old key will be automatically revoked.
    /// </summary>
    public DateTime OldKeyExpiresAt { get; init; }

    /// <summary>
    /// The ID of the old (soon to be expired) API key.
    /// </summary>
    public Guid OldApiKeyId { get; init; }

    /// <summary>
    /// Message indicating the grace period status.
    /// </summary>
    public required string Message { get; init; }
}
