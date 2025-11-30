using Foundation_Lib.Constants;

namespace AuthShared_Lib.Constants;

/// <summary>
/// Authentication and authorization specific error codes.
/// Extends CommonErrorCodes from Foundation_Lib for domain-specific auth errors.
/// </summary>
public static class ErrorCodes
{
    // Inherit common error codes from Foundation_Lib
    // Validation Errors (2000-2999)
    public const string VALIDATION_ERROR = CommonErrorCodes.VALIDATION_ERROR;
    public const string INVALID_EMAIL_FORMAT = CommonErrorCodes.INVALID_EMAIL_FORMAT;
    public const string WEAK_PASSWORD = CommonErrorCodes.WEAK_PASSWORD;
    public const string PASSWORD_MISMATCH = CommonErrorCodes.PASSWORD_MISMATCH;
    public const string REQUIRED_FIELD_MISSING = CommonErrorCodes.REQUIRED_FIELD_MISSING;

    // Authorization Errors (3000-3999)
    public const string UNAUTHORIZED = CommonErrorCodes.UNAUTHORIZED;
    public const string FORBIDDEN = CommonErrorCodes.FORBIDDEN;
    public const string INSUFFICIENT_PERMISSIONS = CommonErrorCodes.INSUFFICIENT_PERMISSIONS;

    // System Errors (5000-5999)
    public const string INTERNAL_SERVER_ERROR = CommonErrorCodes.INTERNAL_SERVER_ERROR;
    public const string DATABASE_ERROR = CommonErrorCodes.DATABASE_ERROR;
    public const string EMAIL_SEND_FAILED = CommonErrorCodes.EMAIL_SEND_FAILED;
    public const string EXTERNAL_SERVICE_ERROR = CommonErrorCodes.EXTERNAL_SERVICE_ERROR;

    // ========================================
    // Authentication-Specific Errors (1000-1999)
    // ========================================

    /// <summary>Invalid email or password provided</summary>
    public const string INVALID_CREDENTIALS = "AUTH_1001";

    /// <summary>User account not found</summary>
    public const string USER_NOT_FOUND = "AUTH_1002";

    /// <summary>Email address already registered</summary>
    public const string USER_ALREADY_EXISTS = "AUTH_1003";

    /// <summary>Email address has not been verified</summary>
    public const string EMAIL_NOT_VERIFIED = "AUTH_1004";

    /// <summary>Account is locked due to failed login attempts</summary>
    public const string ACCOUNT_LOCKED = "AUTH_1005";

    /// <summary>Invalid authentication token</summary>
    public const string INVALID_TOKEN = "AUTH_1006";

    /// <summary>Authentication token has expired</summary>
    public const string TOKEN_EXPIRED = "AUTH_1007";

    /// <summary>Invalid or expired refresh token</summary>
    public const string INVALID_REFRESH_TOKEN = "AUTH_1008";

    /// <summary>Password reset token has expired</summary>
    public const string PASSWORD_RESET_TOKEN_EXPIRED = "AUTH_1009";

    /// <summary>Invalid one-time password (OTP)</summary>
    public const string INVALID_OTP = "AUTH_1010";

    /// <summary>OTP has expired</summary>
    public const string OTP_EXPIRED = "AUTH_1011";

    /// <summary>Too many failed login attempts</summary>
    public const string TOO_MANY_LOGIN_ATTEMPTS = "AUTH_1012";
}
