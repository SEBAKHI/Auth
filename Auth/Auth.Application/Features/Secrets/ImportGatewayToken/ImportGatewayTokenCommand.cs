using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportGatewayToken;

/// <summary>
/// Command to import a caller-supplied gateway token for inter-service authentication
/// (bring-your-own-keys).
/// WARNING: The API Gateway must be reconfigured with the same token; until it is,
/// every proxied request is rejected.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/> bound to
/// a digest of this exact token.
/// </summary>
/// <param name="Token">The gateway token to store.</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this import.</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportGatewayTokenCommand(
    string Token,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
