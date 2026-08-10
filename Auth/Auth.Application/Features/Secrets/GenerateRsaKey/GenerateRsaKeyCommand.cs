using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Command to regenerate the RSA key pair used for JWT signing.
/// WARNING: This invalidates ALL existing access tokens.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/>.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this rotation.</param>
/// <param name="RequestedBy">The administrator performing the rotation.</param>
public record GenerateRsaKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<string>>;
