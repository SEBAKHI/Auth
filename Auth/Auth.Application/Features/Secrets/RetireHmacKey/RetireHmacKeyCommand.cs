using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.RetireHmacKey;

/// <summary>
/// Command to remove a previous HMAC key from the keyring for good. The file is saved
/// twice, so its backup copy loses the key's material as well (KR3).
/// </summary>
/// <param name="KeyId">The previous key to retire.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change, bound to this key.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record RetireHmacKeyCommand(
    int KeyId,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<HmacKeyringDto>>;
