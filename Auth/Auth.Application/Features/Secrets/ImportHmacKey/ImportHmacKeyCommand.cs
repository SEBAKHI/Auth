using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Command to import a caller-supplied HMAC key (bring-your-own-keys): an emergency
/// rotation to that key (S11 keyring alias). WARNING: everything computed under the old
/// keys stops working at once, except webhook keys, which keep validating for 90 days and
/// are re-fingerprinted on their next use.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/> bound to
/// a digest of this exact key material.
/// </summary>
/// <param name="HmacKeyBase64">The HMAC key, base64-encoded (at least 32 bytes / 256 bits).</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this import.</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportHmacKeyCommand(
    string HmacKeyBase64,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
