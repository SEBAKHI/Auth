using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Command to import a caller-supplied HMAC key for refresh token hashing (bring-your-own-keys).
/// WARNING: This replaces the current key and invalidates ALL existing refresh tokens.
/// </summary>
/// <param name="HmacKeyBase64">The HMAC key, base64-encoded (at least 32 bytes / 256 bits).</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportHmacKeyCommand(
    string HmacKeyBase64,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
