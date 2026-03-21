using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Discovery.GetPublicKey;

/// <summary>
/// Query to retrieve the public key in PEM format.
/// </summary>
public record GetPublicKeyQuery : IRequest<ErrorOr<string>>;
