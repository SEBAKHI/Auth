using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.StageHmacKey;

/// <summary>
/// Command to stage a new HMAC key (S11, KR3): it verifies from now on and issues
/// nothing until it is promoted, so staging breaks nothing.
/// </summary>
/// <param name="Value">A key the administrator supplies (base64, at least 256 bits); null to generate one.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record StageHmacKeyCommand(
    string? Value,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<HmacKeyringDto>>;
