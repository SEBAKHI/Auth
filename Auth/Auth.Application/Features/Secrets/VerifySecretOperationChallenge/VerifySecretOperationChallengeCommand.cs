using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.VerifySecretOperationChallenge;

/// <summary>
/// Command to answer a step-up confirmation with the emailed code. On success
/// the approval window opens and the operation's blast radius is returned — the
/// figures an administrator sees immediately before the final confirmation.
/// </summary>
/// <param name="ChallengeId">The challenge being answered.</param>
/// <param name="Code">The six-digit code from the email.</param>
/// <param name="RequestedBy">The administrator answering, for actor binding.</param>
public record VerifySecretOperationChallengeCommand(
    Guid ChallengeId,
    string Code,
    Guid RequestedBy) : IRequest<ErrorOr<SecretRotationImpactDto>>;
