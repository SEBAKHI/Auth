using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Command to import a caller-supplied HMAC key for refresh token hashing (bring-your-own-keys).
/// WARNING: This replaces the current key and invalidates ALL existing refresh tokens, every
/// emailed password-reset link, every in-flight two-factor sign-in, and every webhook key.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/> bound to
/// a digest of this exact key material.
/// </summary>
/// <param name="HmacKeyBase64">The HMAC key, base64-encoded (at least 32 bytes / 256 bits).</param>
/// <param name="ChallengeId">The step-up confirmation authorizing this import.</param>
/// <param name="RequestedBy">The id of the administrator performing the import.</param>
public record ImportHmacKeyCommand(
    string HmacKeyBase64,
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
