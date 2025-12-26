using Auth_Localization.Resources;
using Microsoft.Extensions.Localization;

namespace Auth_Localization.Services;

/// <summary>
/// Service for accessing localized authentication messages.
/// </summary>
public class AuthLocalizer
{
    private readonly IStringLocalizer<AuthMessages> _localizer;

    public AuthLocalizer(IStringLocalizer<AuthMessages> localizer)
    {
        _localizer = localizer;
    }

    public string this[string name] => _localizer[name];

    public string this[string name, params object[] arguments] => _localizer[name, arguments];

    // Authentication Messages
    public string LoginSuccessful => _localizer[nameof(LoginSuccessful)];
    public string LogoutSuccessful => _localizer[nameof(LogoutSuccessful)];
    public string InvalidCredentials => _localizer[nameof(InvalidCredentials)];
    public string AccountLocked => _localizer[nameof(AccountLocked)];
    public string AccountLockedUntil(DateTime until) => _localizer[nameof(AccountLockedUntil), until];
    public string AccountInactive => _localizer[nameof(AccountInactive)];
    public string AccountPending => _localizer[nameof(AccountPending)];
    public string EmailNotConfirmed => _localizer[nameof(EmailNotConfirmed)];
    public string TokenExpired => _localizer[nameof(TokenExpired)];
    public string TokenInvalid => _localizer[nameof(TokenInvalid)];
    public string RefreshTokenExpired => _localizer[nameof(RefreshTokenExpired)];
    public string RefreshTokenRevoked => _localizer[nameof(RefreshTokenRevoked)];
    public string TwoFactorRequired => _localizer[nameof(TwoFactorRequired)];
    public string TwoFactorCodeInvalid => _localizer[nameof(TwoFactorCodeInvalid)];
    public string PasswordChangeRequired => _localizer[nameof(PasswordChangeRequired)];

    // User Messages
    public string UserNotFound => _localizer[nameof(UserNotFound)];
    public string UserCreated => _localizer[nameof(UserCreated)];
    public string UserUpdated => _localizer[nameof(UserUpdated)];
    public string UserDeleted => _localizer[nameof(UserDeleted)];
    public string EmailAlreadyExists => _localizer[nameof(EmailAlreadyExists)];
    public string CannotDeleteSystemUser => _localizer[nameof(CannotDeleteSystemUser)];

    // Password Messages
    public string PasswordChanged => _localizer[nameof(PasswordChanged)];
    public string PasswordTooShort(int minLength) => _localizer[nameof(PasswordTooShort), minLength];
    public string PasswordRequiresUppercase => _localizer[nameof(PasswordRequiresUppercase)];
    public string PasswordRequiresLowercase => _localizer[nameof(PasswordRequiresLowercase)];
    public string PasswordRequiresDigit => _localizer[nameof(PasswordRequiresDigit)];
    public string PasswordRequiresSpecialCharacter => _localizer[nameof(PasswordRequiresSpecialCharacter)];
    public string PasswordRecentlyUsed => _localizer[nameof(PasswordRecentlyUsed)];
    public string CurrentPasswordInvalid => _localizer[nameof(CurrentPasswordInvalid)];

    // Role Messages
    public string RoleNotFound => _localizer[nameof(RoleNotFound)];
    public string RoleCreated => _localizer[nameof(RoleCreated)];
    public string RoleUpdated => _localizer[nameof(RoleUpdated)];
    public string RoleDeleted => _localizer[nameof(RoleDeleted)];
    public string RoleAlreadyAssigned => _localizer[nameof(RoleAlreadyAssigned)];
    public string CannotDeleteSystemRole => _localizer[nameof(CannotDeleteSystemRole)];

    // Permission Messages
    public string PermissionDenied => _localizer[nameof(PermissionDenied)];
    public string PermissionRequired(string permission) => _localizer[nameof(PermissionRequired), permission];

    // General Messages
    public string ValidationError => _localizer[nameof(ValidationError)];
    public string InternalError => _localizer[nameof(InternalError)];
    public string Unauthorized => _localizer[nameof(Unauthorized)];
    public string Forbidden => _localizer[nameof(Forbidden)];
    public string NotFound => _localizer[nameof(NotFound)];
    public string TooManyRequests => _localizer[nameof(TooManyRequests)];
}
