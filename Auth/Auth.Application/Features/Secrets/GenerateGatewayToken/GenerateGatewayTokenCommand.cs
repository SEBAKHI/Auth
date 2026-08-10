using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateGatewayToken;

/// <summary>
/// Command to regenerate the gateway token for inter-service authentication.
/// WARNING: The API Gateway must be reconfigured with the new token; until it
/// is, every proxied request is rejected.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/>.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this rotation.</param>
/// <param name="RequestedBy">The administrator performing the rotation.</param>
public record GenerateGatewayTokenCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<string>>;
