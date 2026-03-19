using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateGatewayToken;

/// <summary>
/// Command to regenerate the gateway token for inter-service authentication.
/// WARNING: The API Gateway must be reconfigured with the new token.
/// </summary>
public record GenerateGatewayTokenCommand(Guid RequestedBy) : IRequest<ErrorOr<string>>;
