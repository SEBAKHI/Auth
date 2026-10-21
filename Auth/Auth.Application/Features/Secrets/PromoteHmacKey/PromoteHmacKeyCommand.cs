using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.PromoteHmacKey;

/// <summary>
/// Command to promote an HMAC key (S11, KR3, KR6).
/// </summary>
/// <param name="Emergency">
/// False: the staged key becomes current, and the old one keeps verifying through its
/// window, so nobody is signed out. True, after a leak: a new key becomes current at
/// once, and everything computed under any older key stops working now, except webhook
/// keys, which keep a 90-day grace.
/// </param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record PromoteHmacKeyCommand(
    bool Emergency,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<HmacKeyringDto>>;
