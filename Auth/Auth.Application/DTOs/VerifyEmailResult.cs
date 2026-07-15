namespace Auth.Application.DTOs;

/// <summary>
/// Result of verifying an email OTP. The anonymous (email-keyed) self-service
/// path completes the sign-in and carries a populated <see cref="Login"/> so the
/// caller is logged in immediately. The admin (user-id-keyed) path only confirms
/// the address and leaves <see cref="Login"/> null — an administrator verifying
/// another user's email must never receive that user's tokens.
/// </summary>
/// <param name="Login">The issued login response for the self-service path, or null for the admin path.</param>
public record VerifyEmailResult(LoginResponse? Login);
