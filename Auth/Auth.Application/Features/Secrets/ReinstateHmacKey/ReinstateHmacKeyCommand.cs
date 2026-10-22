using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ReinstateHmacKey;

/// <summary>
/// Command to make a previous HMAC key current again: the rollback of a promote (KR6).
/// </summary>
/// <param name="KeyId">The previous key to reinstate.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change, bound to this key.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record ReinstateHmacKeyCommand(
    int KeyId,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<HmacKeyringDto>>;
