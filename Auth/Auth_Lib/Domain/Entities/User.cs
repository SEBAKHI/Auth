using Auth_Lib.Domain.Enums;
using Auth_Lib.Domain.Primitives;

namespace Auth_Lib.Domain.Entities;

/// <summary>
/// Represents a user account in the authentication system.
/// </summary>
public class User : AuditableEntityBase
{
    /// <summary>
    /// Gets the user's email address (used for login).
    /// </summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the normalized email address for lookups.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the Argon2id password hash.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the user's first name.
    /// </summary>
    public string FirstName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the user's last name.
    /// </summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the user's display name.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Gets the user's phone number.
    /// </summary>
    public string? PhoneNumber { get; private set; }

    /// <summary>
    /// Gets the user's account status.
    /// </summary>
    public UserStatus Status { get; private set; }

    /// <summary>
    /// Gets whether the email has been confirmed.
    /// </summary>
    public bool EmailConfirmed { get; private set; }

    /// <summary>
    /// Gets whether the phone number has been confirmed.
    /// </summary>
    public bool PhoneConfirmed { get; private set; }

    /// <summary>
    /// Gets whether two-factor authentication is enabled.
    /// </summary>
    public bool TwoFactorEnabled { get; private set; }

    /// <summary>
    /// Gets the two-factor secret key (encrypted).
    /// </summary>
    public string? TwoFactorSecret { get; private set; }

    /// <summary>
    /// Gets the number of consecutive failed login attempts.
    /// </summary>
    public int FailedLoginAttempts { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the account lockout ends.
    /// </summary>
    public DateTime? LockoutEnd { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp of the last successful login.
    /// </summary>
    public DateTime? LastLoginAt { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the password was last changed.
    /// </summary>
    public DateTime? PasswordChangedAt { get; private set; }

    /// <summary>
    /// Gets whether the user must change their password on next login.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// Gets the user's preferred language code.
    /// </summary>
    public string? PreferredLanguage { get; private set; }

    /// <summary>
    /// Gets the user's timezone identifier.
    /// </summary>
    public string? TimeZone { get; private set; }

    /// <summary>
    /// Gets optional metadata as JSON.
    /// </summary>
    public string? Metadata { get; private set; }

    /// <summary>
    /// Gets whether this is a system user (cannot be deleted).
    /// </summary>
    public bool IsSystemUser { get; private set; }

    private User() : base()
    {
    }

    public User(
        Guid id,
        string email,
        string normalizedEmail,
        string passwordHash,
        string firstName,
        string lastName,
        string? displayName,
        string? phoneNumber,
        UserStatus status,
        bool emailConfirmed,
        bool phoneConfirmed,
        bool twoFactorEnabled,
        string? twoFactorSecret,
        int failedLoginAttempts,
        DateTime? lockoutEnd,
        DateTime? lastLoginAt,
        DateTime? passwordChangedAt,
        bool mustChangePassword,
        string? preferredLanguage,
        string? timeZone,
        string? metadata,
        bool isSystemUser,
        DateTime createdAt,
        Guid createdBy,
        DateTime? modifiedAt,
        Guid? modifiedBy) : base(id)
    {
        Email = email;
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        DisplayName = displayName;
        PhoneNumber = phoneNumber;
        Status = status;
        EmailConfirmed = emailConfirmed;
        PhoneConfirmed = phoneConfirmed;
        TwoFactorEnabled = twoFactorEnabled;
        TwoFactorSecret = twoFactorSecret;
        FailedLoginAttempts = failedLoginAttempts;
        LockoutEnd = lockoutEnd;
        LastLoginAt = lastLoginAt;
        PasswordChangedAt = passwordChangedAt;
        MustChangePassword = mustChangePassword;
        PreferredLanguage = preferredLanguage;
        TimeZone = timeZone;
        Metadata = metadata;
        IsSystemUser = isSystemUser;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ModifiedAt = modifiedAt;
        ModifiedBy = modifiedBy;
    }

    public static User Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        Guid createdBy,
        string? displayName = null,
        string? phoneNumber = null,
        string preferredLanguage = "en",
        string timeZone = "UTC")
    {
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName ?? $"{firstName} {lastName}",
            PhoneNumber = phoneNumber,
            Status = UserStatus.Active,
            EmailConfirmed = false,
            PhoneConfirmed = false,
            TwoFactorEnabled = false,
            FailedLoginAttempts = 0,
            MustChangePassword = false,
            PreferredLanguage = preferredLanguage,
            TimeZone = timeZone,
            IsSystemUser = false,
            PasswordChangedAt = DateTime.UtcNow
        };
        user.SetCreated(createdBy);
        return user;
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? displayName,
        string? phoneNumber,
        string? preferredLanguage,
        string? timeZone,
        Guid modifiedBy)
    {
        FirstName = firstName;
        LastName = lastName;
        DisplayName = displayName;
        PhoneNumber = phoneNumber;
        PreferredLanguage = preferredLanguage;
        TimeZone = timeZone;
        SetModified(modifiedBy);
    }

    public void ChangePassword(string newPasswordHash, Guid modifiedBy)
    {
        PasswordHash = newPasswordHash;
        PasswordChangedAt = DateTime.UtcNow;
        MustChangePassword = false;
        SetModified(modifiedBy);
    }

    public void RecordSuccessfulLogin()
    {
        LastLoginAt = DateTime.UtcNow;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
    }

    public void RecordFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            Status = UserStatus.Locked;
            LockoutEnd = DateTime.UtcNow.Add(lockoutDuration);
        }
    }

    public void Unlock(Guid modifiedBy)
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        SetModified(modifiedBy);
    }

    public void Lock(DateTime? lockoutEnd, Guid modifiedBy)
    {
        Status = UserStatus.Locked;
        LockoutEnd = lockoutEnd;
        SetModified(modifiedBy);
    }

    public void Activate(Guid modifiedBy)
    {
        Status = UserStatus.Active;
        SetModified(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        Status = UserStatus.Inactive;
        SetModified(modifiedBy);
    }

    public void ConfirmEmail(Guid modifiedBy)
    {
        EmailConfirmed = true;
        SetModified(modifiedBy);
    }

    public void ConfirmPhone(Guid modifiedBy)
    {
        PhoneConfirmed = true;
        SetModified(modifiedBy);
    }

    public void EnableTwoFactor(string secret, Guid modifiedBy)
    {
        TwoFactorEnabled = true;
        TwoFactorSecret = secret;
        SetModified(modifiedBy);
    }

    public void DisableTwoFactor(Guid modifiedBy)
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        SetModified(modifiedBy);
    }

    public void RequirePasswordChange(Guid modifiedBy)
    {
        MustChangePassword = true;
        SetModified(modifiedBy);
    }

    public bool IsLockedOut()
    {
        return Status == UserStatus.Locked &&
               (LockoutEnd == null || LockoutEnd > DateTime.UtcNow);
    }

    public string GetFullName() => $"{FirstName} {LastName}";
}
