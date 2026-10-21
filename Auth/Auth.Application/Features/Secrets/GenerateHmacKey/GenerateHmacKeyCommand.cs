using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Command to regenerate the HMAC key every opaque credential is fingerprinted under:
/// an emergency rotation (S11 keyring alias). WARNING: everything computed under the
/// old keys stops working at once (refresh tokens and SSO sessions, reset and
/// invitation links, emailed codes, in-flight sign-ins), except webhook keys, which
/// keep validating for 90 days and are re-fingerprinted on their next use.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/>.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this rotation.</param>
/// <param name="RequestedBy">The administrator performing the rotation.</param>
public record GenerateHmacKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
