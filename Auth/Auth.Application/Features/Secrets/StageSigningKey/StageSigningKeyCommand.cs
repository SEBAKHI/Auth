using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.StageSigningKey;

/// <summary>
/// Command to stage a new signing key (S12, KR3): it is published and accepted from the next
/// start of the API, and signs nothing until it is promoted, so staging breaks nothing.
/// </summary>
/// <param name="Value">A private key the administrator supplies (PEM, RSA, at least 2048 bits); null to generate one.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this change.</param>
/// <param name="RequestedBy">The administrator making the change.</param>
public record StageSigningKeyCommand(
    string? Value,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<SigningKeyringDto>>;
