namespace Auth.Application.DTOs;

/// <summary>
/// Response returned after successful user registration.
/// </summary>
/// <param name="UserId">The newly created user's ID.</param>
/// <param name="MaskedEmail">The masked email address where verification was sent.</param>
/// <param name="Message">Human-readable message about next steps.</param>
/// <param name="OrganizationCreated">Whether a personal organization was created during registration.</param>
/// <param name="VerificationCodeExpiresAt">When the just-sent verification code expires, letting the client show a countdown without requesting a fresh code. Null if the verification email failed to send.</param>
public record RegisterResponse(
    Guid UserId,
    string MaskedEmail,
    string Message,
    bool OrganizationCreated = false,
    DateTime? VerificationCodeExpiresAt = null);
