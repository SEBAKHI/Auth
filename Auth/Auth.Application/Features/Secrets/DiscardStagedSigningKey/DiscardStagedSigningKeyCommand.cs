using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.DiscardStagedSigningKey;

/// <summary>
/// Command to remove the staged signing key before it is promoted (S12). Nothing was signed
/// with it. The keyring remembers it, so it can never be staged again.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record DiscardStagedSigningKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<SigningKeyringDto>>;
