using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Command to regenerate the HMAC key used for refresh token hashing.
/// WARNING: This invalidates ALL existing refresh tokens.
/// </summary>
public record GenerateHmacKeyCommand(Guid RequestedBy) : IRequest<ErrorOr<Success>>;
