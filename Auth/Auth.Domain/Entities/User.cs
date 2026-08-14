using Auth.Domain.Enums;
using Auth.Domain.Events;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;

namespace Auth.Domain.Entities;

/// <summary>
/// Represents a user account in the authentication system.
/// </summary>
public class User : AggregateRoot
{
    /// <summary>
    /// Gets the user's email address (used for login).
    /// </summary>
    public Email Email { get; private set; } = Email.From(string.Empty);

    /// <summary>
    /// Gets the normalized email address for lookups.
    /// </summary>
    public string NormalizedEmail { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the Argon2id password hash.
    /// Null for users who authenticate exclusively via external providers (Google, Apple, etc.).
    /// </summary>
    public string? PasswordHash { get; private set; }

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
    public PhoneNumber? PhoneNumber { get; private set; }

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
    /// Gets the user's preferred UI theme (light, dark, or system).
    /// </summary>
    public string? Theme { get; private set; }

    /// <summary>
    /// Gets optional metadata as JSON.
    /// </summary>
    public string? Metadata { get; private set; }

    /// <summary>
    /// Gets whether this is a system user (cannot be deleted).
    /// </summary>
    public bool IsSystemUser { get; private set; }

    /// <summary>
    /// Gets the storage key (or absolute URL) of the user's profile image; null when unset.
    /// </summary>
    public string? ProfileImageUrl { get; private set; }

    /// <summary>
    /// Gets the IP address recorded at the last successful login.
    /// </summary>
    public string? LastLoginIp { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the current password expires; null when no expiry policy applies.
    /// </summary>
    public DateTime? PasswordExpiresUtc { get; private set; }

    /// <summary>
    /// Gets whether the account has been soft-deleted. Soft-deleted accounts are
    /// hidden from operational reads and keep their email reserved; they are the
    /// only accounts eligible for permanent (hard) deletion.
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the account was soft-deleted; null while the account is live.
    /// </summary>
    public DateTime? DeletedAt { get; private set; }

    private User() : base()
    {
    }

    public User(
        Guid id,
        string email,
        string normalizedEmail,
        string? passwordHash,
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
        Guid? modifiedBy,
        string? profileImageUrl = null,
        string? lastLoginIp = null,
        DateTime? passwordExpiresUtc = null,
        string? theme = "system",
        bool isDeleted = false,
        DateTime? deletedAt = null) : base(id)
    {
        Email = Email.From(email);
        NormalizedEmail = normalizedEmail;
        PasswordHash = passwordHash;
        FirstName = firstName;
        LastName = lastName;
        DisplayName = displayName;
        PhoneNumber = ValueObjects.PhoneNumber.FromNullable(phoneNumber);
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
        ProfileImageUrl = profileImageUrl;
        LastLoginIp = lastLoginIp;
        PasswordExpiresUtc = passwordExpiresUtc;
        Theme = theme;
        IsDeleted = isDeleted;
        DeletedAt = deletedAt;
    }

    public static User Create(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        Guid createdBy,
        string? phoneNumber = null,
        string preferredLanguage = "en",
        string timeZone = "UTC",
        string theme = "system")
    {
        var emailVo = Email.From(email.ToLowerInvariant());
        var user = new User
        {
            Email = emailVo,
            NormalizedEmail = emailVo.ToNormalized(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = $"{firstName} {lastName}",
            PhoneNumber = ValueObjects.PhoneNumber.FromNullable(phoneNumber),
            Status = UserStatus.Active,
            EmailConfirmed = false,
            PhoneConfirmed = false,
            TwoFactorEnabled = false,
            FailedLoginAttempts = 0,
            MustChangePassword = false,
            PreferredLanguage = preferredLanguage,
            TimeZone = timeZone,
            Theme = theme,
            IsSystemUser = false,
            PasswordChangedAt = DateTime.UtcNow
        };
        user.SetCreated(createdBy);
        user.RaiseDomainEvent(new UserCreatedEvent(user.Id, user.Email, user.FirstName, user.LastName, createdBy));
        return user;
    }

    /// <summary>
    /// Creates a new user from an external authentication provider (Google, Apple, etc.).
    /// Email is considered verified by the provider. No local password is set.
    /// </summary>
    public static User CreateFromExternalProvider(
        string email,
        string firstName,
        string lastName,
        Guid createdBy,
        string? profileImageUrl = null,
        string preferredLanguage = "en",
        string timeZone = "UTC",
        string theme = "system")
    {
        var emailVo = Email.From(email.ToLowerInvariant());
        var user = new User
        {
            Email = emailVo,
            NormalizedEmail = emailVo.ToNormalized(),
            PasswordHash = null,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = $"{firstName} {lastName}",
            Status = UserStatus.Active,
            EmailConfirmed = true,
            PhoneConfirmed = false,
            TwoFactorEnabled = false,
            FailedLoginAttempts = 0,
            MustChangePassword = false,
            PreferredLanguage = preferredLanguage,
            TimeZone = timeZone,
            Theme = theme,
            IsSystemUser = false,
            ProfileImageUrl = profileImageUrl
        };
        user.SetCreated(createdBy);
        user.RaiseDomainEvent(new UserCreatedEvent(user.Id, user.Email, user.FirstName, user.LastName, createdBy));
        return user;
    }

    /// <summary>Sets the profile image storage key (or absolute URL).</summary>
    public void SetProfileImage(string imageKey, Guid modifiedBy)
    {
        ProfileImageUrl = imageKey;
        SetModified(modifiedBy);
    }

    /// <summary>Clears the profile image.</summary>
    public void RemoveProfileImage(Guid modifiedBy)
    {
        ProfileImageUrl = null;
        SetModified(modifiedBy);
    }

    public void UpdateProfile(
        string firstName,
        string lastName,
        string? phoneNumber,
        string? preferredLanguage,
        string? timeZone,
        string? theme,
        Guid modifiedBy)
    {
        FirstName = firstName;
        LastName = lastName;
        // Derived, never supplied: the store keeps FullName as a computed column
        // over these two, so a display name that could differ from it would be a
        // second copy that no write ever reaches. GetFullName() is used verbatim
        // so this value and the next read of it are the same string.
        DisplayName = GetFullName();
        PhoneNumber = ValueObjects.PhoneNumber.FromNullable(phoneNumber);
        PreferredLanguage = preferredLanguage;
        TimeZone = timeZone;
        Theme = theme;
        SetModified(modifiedBy);
    }

    public void ChangePassword(string newPasswordHash, Guid modifiedBy)
    {
        PasswordHash = newPasswordHash;
        PasswordChangedAt = DateTime.UtcNow;
        MustChangePassword = false;
        SetModified(modifiedBy);
        RaiseDomainEvent(new PasswordChangedEvent(Id, modifiedBy));
    }

    public void RecordSuccessfulLogin(string? ipAddress = null, string? userAgent = null)
    {
        LastLoginAt = DateTime.UtcNow;
        LastLoginIp = ipAddress;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        RaiseDomainEvent(new UserLoggedInEvent(Id, Email, ipAddress, userAgent));
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
        RaiseDomainEvent(new UserUnlockedEvent(Id, modifiedBy));
    }

    public void Lock(DateTime? lockoutEnd, Guid modifiedBy)
    {
        Status = UserStatus.Locked;
        LockoutEnd = lockoutEnd;
        SetModified(modifiedBy);
        RaiseDomainEvent(new UserLockedEvent(Id, lockoutEnd, modifiedBy));
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
        RaiseDomainEvent(new TwoFactorEnabledEvent(Id, modifiedBy));
    }

    public void DisableTwoFactor(Guid modifiedBy)
    {
        TwoFactorEnabled = false;
        TwoFactorSecret = null;
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorDisabledEvent(Id, modifiedBy));
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
