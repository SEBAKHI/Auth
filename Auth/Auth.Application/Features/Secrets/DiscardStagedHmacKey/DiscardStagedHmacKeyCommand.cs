using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DiscardStagedHmacKey;

/// <summary>
/// Command to remove the staged HMAC key before it is promoted: the rollback of a stage
/// (KR6). Nothing was issued under it, so nothing breaks.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record DiscardStagedHmacKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<HmacKeyringDto>>;
