using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetJwks;

/// <summary>
/// Query to retrieve the JSON Web Key Set (JWKS) for token validation.
/// </summary>
public record GetJwksQuery : IRequest<ErrorOr<string>>;
