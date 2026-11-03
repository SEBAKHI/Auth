using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Command to regenerate the RSA key pair used for JWT signing: an alias of a stage and an
/// emergency promote (S12), kept until SUNSET(S12).
/// WARNING: from the next start of the API, ALL existing access tokens are refused.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/>.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this rotation.</param>
/// <param name="RequestedBy">The administrator performing the rotation.</param>
public record GenerateRsaKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<string>>;
