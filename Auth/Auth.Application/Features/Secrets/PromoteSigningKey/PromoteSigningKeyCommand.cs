using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.PromoteSigningKey;

/// <summary>
/// Command to promote the staged signing key (S12, KR3). Scheduled: it signs from now on, and
/// the old key is still accepted for about a day, so nobody is signed out. Emergency: it signs
/// at once and the old key, marked compromised, is neither accepted nor published.
/// </summary>
/// <param name="Emergency">True after a leak of the current key.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record PromoteSigningKeyCommand(
    bool Emergency,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<SigningKeyringDto>>;
