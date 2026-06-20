using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportGatewayToken;

/// <summary>
/// Command to import a caller-supplied gateway token for inter-service authentication
/// (bring-your-own-keys).
/// WARNING: The API Gateway must be reconfigured with the same token.
/// </summary>
/// <param name="Token">The gateway token to store.</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportGatewayTokenCommand(
    string Token,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
