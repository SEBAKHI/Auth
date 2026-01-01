namespace Auth_API.Modules.Authentication.Contracts;

/// <summary>
/// Request model for sending email verification OTP.
/// </summary>
/// <remarks>
/// This endpoint requires authentication.
/// The user ID is extracted from the JWT token.
/// </remarks>
public record SendEmailVerificationRequest
{
    // No additional properties needed.
    // UserId is extracted from the authenticated user's token.
}
