using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateRsaKey;

/// <summary>
/// Command to regenerate the RSA key pair used for JWT signing.
/// WARNING: This invalidates ALL existing access tokens.
/// </summary>
public record GenerateRsaKeyCommand(Guid RequestedBy) : IRequest<ErrorOr<string>>;
