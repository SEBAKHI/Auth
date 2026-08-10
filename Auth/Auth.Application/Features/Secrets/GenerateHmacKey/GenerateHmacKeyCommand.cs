using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Secrets.GenerateHmacKey;

/// <summary>
/// Command to regenerate the HMAC key used for refresh token hashing.
/// WARNING: This invalidates ALL existing refresh tokens, every emailed
/// password-reset link, every in-flight two-factor sign-in, and every webhook
/// key — all four are hashed with this one key.
/// Requires a verified <see cref="Domain.Entities.SecretOperationChallenge"/>.
/// </summary>
/// <param name="ChallengeId">The step-up confirmation authorizing this rotation.</param>
/// <param name="RequestedBy">The administrator performing the rotation.</param>
public record GenerateHmacKeyCommand(
    Guid ChallengeId,
    Guid RequestedBy) : IRequest<ErrorOr<Success>>;
