using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors related to authentication operations.
/// </summary>
public static class AuthErrors
{
    public static Error InvalidToken => Error.Validation(
        code: "Auth.InvalidToken",
        description: "The provided token is invalid.");

    public static Error TokenExpired => Error.Validation(
        code: "Auth.TokenExpired",
        description: "The provided token has expired.");

    public static Error TokenRevoked => Error.Forbidden(
        code: "Auth.TokenRevoked",
        description: "The provided token has been revoked.");

    public static Error RefreshTokenNotFound => Error.NotFound(
        code: "Auth.RefreshTokenNotFound",
        description: "The refresh token was not found.");

    public static Error RefreshTokenExpired => Error.Validation(
        code: "Auth.RefreshTokenExpired",
        description: "The refresh token has expired. Please log in again.");

    public static Error RefreshTokenRevoked => Error.Forbidden(
        code: "Auth.RefreshTokenRevoked",
        description: "The refresh token has been revoked.");

    public static Error SessionNotFound => Error.NotFound(
        code: "Auth.SessionNotFound",
        description: "The session was not found.");

    public static Error SessionExpired => Error.Validation(
        code: "Auth.SessionExpired",
        description: "Your session has expired. Please log in again.");

    public static Error SessionTerminated => Error.Forbidden(
        code: "Auth.SessionTerminated",
        description: "Your session has been terminated.");

    public static Error InvalidGatewayToken => Error.Forbidden(
        code: "Auth.InvalidGatewayToken",
        description: "Invalid or missing gateway token. Direct API access is not allowed.");

    public static Error Unauthorized => Error.Forbidden(
        code: "Auth.Unauthorized",
        description: "You are not authorized to perform this action.");

    public static Error PermissionDenied(string permission) => Error.Forbidden(
        code: "Auth.PermissionDenied",
        description: $"You do not have the required permission: '{permission}'.");

    public static Error ApplicationNotFound(Guid applicationId) => Error.NotFound(
        code: "Auth.ApplicationNotFound",
        description: $"Application with ID '{applicationId}' was not found.");

    public static Error ApplicationInactive => Error.Forbidden(
        code: "Auth.ApplicationInactive",
        description: "This application is currently inactive.");

    public static Error InvalidSigningKey => Error.Failure(
        code: "Auth.InvalidSigningKey",
        description: "The JWT signing key is invalid or not configured.");

    public static Error KeyGenerationFailed => Error.Failure(
        code: "Auth.KeyGenerationFailed",
        description: "Failed to generate cryptographic keys.");

    public static Error TokenGenerationFailed => Error.Failure(
        code: "Auth.TokenGenerationFailed",
        description: "Failed to generate authentication token.");

    public static Error ConcurrentLoginDetected => Error.Forbidden(
        code: "Auth.ConcurrentLoginDetected",
        description: "A new login was detected from another device. This session has been terminated.");
}
