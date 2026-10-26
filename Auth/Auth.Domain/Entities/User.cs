using Auth.Domain.Constants;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using ErrorOr;

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
    /// Gets the user's display name: the first and last name, or null for an account
    /// that has neither (OI-215). Never blank: the store computes it with a space
    /// between the two, and a space is not a name.
    /// </summary>
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Gets the name the platform shows and mails for this account: its name when it
    /// has one, otherwise its address (OI-215). The one server rule for "the name to
    /// show"; never blank.
    /// </summary>
    public string NameToShow => DisplayNameFor(FirstName, LastName) ?? Email.Value;

    /// <summary>
    /// <see cref="NameToShow"/> for a read model that carries the stored display name
    /// (the computed FullName column) instead of the account: the name when it is not
    /// blank, otherwise the address.
    /// </summary>
    public static string NameToShowFrom(string? storedDisplayName, string email) =>
        string.IsNullOrWhiteSpace(storedDisplayName) ? email : storedDisplayName.Trim();

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
    /// Gets whether two-factor authentication is enabled — the account flag the
    /// sign-in gate reads.
    /// </summary>
    /// <remarks>
    /// Written to the database only together with the user's two-factor row, in
    /// one transaction of <c>ITwoFactorStateStore</c>; the whole-row user write
    /// leaves it alone, so a caller holding a stale copy cannot switch it off.
    /// </remarks>
    public bool TwoFactorEnabled { get; private set; }

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
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        PhoneNumber = ValueObjects.PhoneNumber.FromNullable(phoneNumber);
        Status = status;
        EmailConfirmed = emailConfirmed;
        PhoneConfirmed = phoneConfirmed;
        TwoFactorEnabled = twoFactorEnabled;
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

    /// <summary>
    /// Creates an account. <paramref name="passwordHash"/> is null only for an account
    /// that signs up without a password (OI-215, <c>Registration:PasswordAtSignUp</c>
    /// off): it signs in with a code emailed to its address. The names may be empty
    /// when the sign-up did not ask for them.
    /// </summary>
    public static User Create(
        string email,
        string? passwordHash,
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
            DisplayName = DisplayNameFor(firstName, lastName),
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
            PasswordChangedAt = passwordHash is null ? null : DateTime.UtcNow
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
            DisplayName = DisplayNameFor(firstName, lastName),
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
        // second copy that no write ever reaches. Derived by the same rule the
        // next read applies to that column, so the two are the same value.
        DisplayName = DisplayNameFor(firstName, lastName);
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
        RaiseDomainEvent(new PasswordChangedEvent(Id, modifiedBy, Email, NameToShow));
    }

    /// <summary>
    /// Sets the first password on an account that has never had one - the path an
    /// external-only sign-up (Google, Apple) takes to acquire local credentials.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="ChangePassword"/> for two reasons. Its caller cannot prove
    /// knowledge of a current password, because there is none; and the event it raises has to be
    /// distinguishable in the audit log from a rotation. The "never had one" invariant lives here
    /// rather than in a handler so that no future caller can overwrite an existing password
    /// through a path that asks for nothing.
    /// </remarks>
    public ErrorOr<Success> SetInitialPassword(string passwordHash, Guid modifiedBy)
    {
        if (PasswordHash is not null)
            return UserErrors.PasswordAlreadySet;

        PasswordHash = passwordHash;
        PasswordChangedAt = DateTime.UtcNow;
        MustChangePassword = false;
        SetModified(modifiedBy);
        RaiseDomainEvent(new PasswordCreatedEvent(Id, modifiedBy, Email, NameToShow));
        return Result.Success;
    }

    /// <summary>
    /// Removes the password at its owner's request (OI-215, D-215-3): from then on the
    /// account signs in with a code emailed to its address.
    /// </summary>
    /// <remarks>
    /// The conditions an aggregate can judge live here: there is a password, and the
    /// address is confirmed, since the code goes to that address and nothing else
    /// could let the account back in. The others are the handler's: email sending on,
    /// a recent sign-in that proved the password (and the authenticator app when it
    /// is on), and no emailed second factor. The change stamp is written as for any
    /// password change by the owner.
    /// </remarks>
    public ErrorOr<Success> RemovePassword(Guid modifiedBy)
    {
        if (PasswordHash is null)
        {
            return UserErrors.PasswordNotSet;
        }

        if (!EmailConfirmed)
        {
            return UserErrors.EmailNotConfirmed;
        }

        RetirePassword();
        PasswordChangedAt = DateTime.UtcNow;
        SetModified(modifiedBy);
        RaiseDomainEvent(new PasswordRemovedEvent(Id, modifiedBy, Email, NameToShow, PasswordRemovalReasons.Owner));
        return Result.Success;
    }

    /// <summary>
    /// Confirms the address through a sign-in by emailed code, the first proof that
    /// whoever signs in holds the mailbox (OI-215, D-215-5 (a)). A password set before
    /// that proof is removed with the confirmation: an administrator chose it, or a
    /// stranger typed it before the address was proven, and either way its holder was
    /// never shown to own the mailbox. The sign-in then continues as a passwordless one.
    /// </summary>
    /// <remarks>
    /// The change stamp is left as it is: the person proved the mailbox, not a
    /// password, and the stamp is what the administrator's reset of a locked second
    /// factor reads as the owner's own password change.
    /// </remarks>
    /// <param name="modifiedBy">The account itself.</param>
    /// <returns>Whether a password was removed; nothing changes for a confirmed address.</returns>
    public bool ConfirmEmailBySignInCode(Guid modifiedBy)
    {
        if (EmailConfirmed)
        {
            return false;
        }

        EmailConfirmed = true;
        var removed = PasswordHash is not null;
        if (removed)
        {
            RetirePassword();
            RaiseDomainEvent(new PasswordRemovedEvent(
                Id, modifiedBy, Email, NameToShow, PasswordRemovalReasons.SetBeforeAddressProven));
        }

        SetModified(modifiedBy);
        return removed;
    }

    // An account without a password has nothing to change or let expire: a forced
    // change would ask for a password that no longer exists, a dead end.
    private void RetirePassword()
    {
        PasswordHash = null;
        MustChangePassword = false;
        PasswordExpiresUtc = null;
    }

    /// <summary>
    /// Records that an external provider identity was attached to this existing account.
    /// </summary>
    /// <remarks>
    /// Nothing on the user changes - the link itself is a separate entity. This exists so the
    /// event has an aggregate to be raised from: UserExternalLogin is an EntityBase, and
    /// RaiseDomainEvent is protected, so no code outside an aggregate can announce this.
    /// </remarks>
    public void RecordExternalProviderLink(
        string provider,
        string providerUserId,
        bool holdsWildcardPermission)
    {
        RaiseDomainEvent(new ExternalProviderLinkedEvent(
            Id, Email, provider, providerUserId, holdsWildcardPermission));
    }

    /// <summary>
    /// Records a completed sign-in on the aggregate and raises
    /// <see cref="UserLoggedInEvent"/>. The lock columns are not touched: whether a
    /// sign-in lifts an automatic lock depends on whether it proved the password,
    /// and that is written by the one statement that records the success.
    /// </summary>
    public void RecordSuccessfulLogin(string? ipAddress = null, string? userAgent = null)
    {
        LastLoginAt = DateTime.UtcNow;
        LastLoginIp = ipAddress;
        RaiseDomainEvent(new UserLoggedInEvent(Id, Email, ipAddress, userAgent));
    }

    /// <summary>
    /// An administrator lifts any lock: active, a count of 0 and no expiry.
    /// </summary>
    /// <returns>The lock columns to write.</returns>
    public AccountStatusChange Unlock(Guid modifiedBy)
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        SetModified(modifiedBy);
        RaiseDomainEvent(new UserUnlockedEvent(Id, modifiedBy));
        return AccountStatusChange.Unlock();
    }

    /// <summary>
    /// An administrator locks the account, with or without an expiry.
    /// </summary>
    /// <returns>The lock columns to write.</returns>
    public AccountStatusChange Lock(DateTime? lockoutEnd, Guid modifiedBy)
    {
        Status = UserStatus.Locked;
        LockoutEnd = lockoutEnd;
        // An administrative lock starts a new episode: the counter is what marks
        // a lock as raised by wrong passwords (AccountLockState.Kind), so an
        // administrator's lock placed during a brute-force run must not inherit
        // the attacker's count and read as automatic — that is the difference
        // between a lock a familiar device may pass and one nobody may. The
        // forensic history stays in LoginAttempts.
        FailedLoginAttempts = 0;
        SetModified(modifiedBy);
        RaiseDomainEvent(new UserLockedEvent(Id, lockoutEnd, modifiedBy));
        return AccountStatusChange.Lock(lockoutEnd);
    }

    /// <summary>
    /// An administrator activates the account. Like an unlock, it clears any lock and
    /// the failure count: activating is an administrator's decision that the account
    /// is usable now.
    /// </summary>
    /// <returns>The lock columns to write.</returns>
    public AccountStatusChange Activate(Guid modifiedBy)
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        SetModified(modifiedBy);
        return AccountStatusChange.Activate();
    }

    /// <summary>
    /// An administrator deactivates the account. The lock columns are left as they
    /// are; activation clears them.
    /// </summary>
    /// <returns>The status to write.</returns>
    public AccountStatusChange Deactivate(Guid modifiedBy)
    {
        Status = UserStatus.Inactive;
        SetModified(modifiedBy);
        return AccountStatusChange.Deactivate();
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

    /// <summary>
    /// Records that two-factor authentication was switched on. Called only once the
    /// store has committed the change together with the user's factor row, so the
    /// event reports a change that happened, never one that lost a race.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void EnableTwoFactor(Guid modifiedBy, string? deviceName)
    {
        TwoFactorEnabled = true;
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorEnabledEvent(Id, modifiedBy, Email, NameToShow, deviceName));
    }

    /// <summary>
    /// Records that two-factor authentication was switched off. Called only once the
    /// store has committed the change together with the user's factor row, so the
    /// event reports a change that happened, never one that lost a race.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void DisableTwoFactor(Guid modifiedBy, string? deviceName)
    {
        TwoFactorEnabled = false;
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorDisabledEvent(Id, modifiedBy, Email, NameToShow, deviceName));
    }

    /// <summary>
    /// Records that the emailed code became the second factor (S24). Called only once
    /// the store has committed the factor row and the account flag together. When it
    /// took the place of an authenticator app, the app's removal is recorded first,
    /// marked as replaced, so the owner is told once.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    /// <param name="replacedAuthenticator">Whether an authenticator app was bound until now.</param>
    public void EnableEmailCodeFactor(Guid modifiedBy, string? deviceName, bool replacedAuthenticator)
    {
        TwoFactorEnabled = true;
        SetModified(modifiedBy);

        var displayName = NameToShow;
        if (replacedAuthenticator)
        {
            RaiseDomainEvent(new TwoFactorDisabledEvent(
                Id, modifiedBy, Email, displayName, deviceName, ReplacedByEmailCode: true));
        }

        RaiseDomainEvent(new TwoFactorEmailCodeEnabledEvent(
            Id, modifiedBy, Email, displayName, deviceName, replacedAuthenticator));
    }

    /// <summary>
    /// Records that the owner switched the emailed code off (S24). Called only once
    /// the store has removed the factor row and cleared the account flag together.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void DisableEmailCodeFactor(Guid modifiedBy, string? deviceName)
    {
        TwoFactorEnabled = false;
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorEmailCodeDisabledEvent(
            Id, modifiedBy, Email, NameToShow, deviceName, TwoFactorEmailCodeDisableReasons.User));
    }

    /// <summary>
    /// Records that an authenticator app took the place of the emailed code (S24).
    /// Called only once the store has committed the switch in one transaction. The
    /// emailed code's removal is recorded first, marked as replaced, so the owner is
    /// told once, by the app's notice.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void ReplaceEmailCodeWithAuthenticator(Guid modifiedBy, string? deviceName)
    {
        TwoFactorEnabled = true;
        SetModified(modifiedBy);

        var displayName = NameToShow;
        RaiseDomainEvent(new TwoFactorEmailCodeDisabledEvent(
            Id, modifiedBy, Email, displayName, deviceName, TwoFactorEmailCodeDisableReasons.ReplacedByAuthenticator));
        RaiseDomainEvent(new TwoFactorEnabledEvent(Id, modifiedBy, Email, displayName, deviceName, ReplacedEmailCode: true));
    }

    /// <summary>
    /// Records that the recovery codes of the second factor were replaced with a
    /// new set. Called only once the store has committed the new set, so the event
    /// reports a change that happened.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void RegenerateTwoFactorRecoveryCodes(Guid modifiedBy, string? deviceName)
    {
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorRecoveryCodesRegeneratedEvent(Id, modifiedBy, Email, NameToShow, deviceName));
    }

    /// <summary>
    /// Records that the second factor moved to a new authenticator app. Called only
    /// once the store has committed the new secret, so the event reports a change
    /// that happened. The factor stays on throughout.
    /// </summary>
    /// <param name="modifiedBy">The account that made the change.</param>
    /// <param name="deviceName">The browser and operating system it was made from, when known.</param>
    public void ReplaceTwoFactorAuthenticator(Guid modifiedBy, string? deviceName)
    {
        SetModified(modifiedBy);
        RaiseDomainEvent(new TwoFactorAuthenticatorReplacedEvent(Id, modifiedBy, Email, NameToShow, deviceName));
    }

    /// <summary>
    /// The part of "may this administrator reset this account's second factor"
    /// the account itself decides: never its own (an administrator who lost the
    /// factor asks another one, or the owner's emergency script), and never the
    /// internal system account. Whether the administrator's authority covers the
    /// account's is the other part, which needs both accounts' grants.
    /// </summary>
    /// <param name="actorId">The administrator asking to reset the factor.</param>
    public ErrorOr<Success> EnsureTwoFactorResettableBy(Guid actorId) =>
        IsTwoFactorResettableBy(Id, actorId) ? Result.Success : TwoFactorErrors.ResetNotPermitted;

    /// <summary>
    /// The rule <see cref="EnsureTwoFactorResettableBy"/> applies, by id alone: never
    /// the administrator's own account, never the system account. The one definition,
    /// also asked by the account rules when they tell the console whether to offer the
    /// reset, where no loaded account is at hand.
    /// </summary>
    /// <param name="accountId">The account whose factor would be reset.</param>
    /// <param name="actorId">The administrator asking.</param>
    public static bool IsTwoFactorResettableBy(Guid accountId, Guid actorId) =>
        actorId != accountId && accountId != Constants.WellKnownUserIds.System;

    /// <summary>
    /// Records that an administrator removed this account's second factor. Called
    /// only once the store has deleted the factor and cleared the flag, so the
    /// event reports a change that happened, and only after
    /// <see cref="EnsureTwoFactorResettableBy"/> allowed it.
    /// </summary>
    /// <param name="resetBy">The administrator who removed the factor.</param>
    public void ResetTwoFactor(Guid resetBy)
    {
        TwoFactorEnabled = false;
        SetModified(resetBy);
        RaiseDomainEvent(new TwoFactorResetEvent(Id, resetBy, Email, NameToShow));
    }

    public void RequirePasswordChange(Guid modifiedBy)
    {
        MustChangePassword = true;
        SetModified(modifiedBy);
    }

    /// <summary>
    /// The three lock columns as this aggregate was read.
    /// </summary>
    public AccountLockState LockState => new(Status, FailedLoginAttempts, LockoutEnd);

    /// <summary>
    /// Which lock the account holds at <paramref name="nowUtc"/>: the one classifier
    /// every sign-in, renewal and recovery path asks (<see cref="AccountLockState.Kind"/>).
    /// </summary>
    public AccountLockKind LockKind(DateTime nowUtc) => LockState.Kind(nowUtc);

    /// <inheritdoc cref="AccountLockState.IsFamiliarDoorSealed"/>
    public bool IsFamiliarDoorSealed(int hardLimit, int lockThreshold) =>
        LockState.IsFamiliarDoorSealed(hardLimit, lockThreshold);

    /// <inheritdoc cref="AccountLockState.LockRefusal"/>
    public Error LockRefusal(AccountLockKind kind) => LockState.LockRefusal(kind);

    /// <summary>
    /// Whether an already-authenticated principal may have its credentials
    /// renewed — a refresh grant, an OIDC authorize against a live SSO session,
    /// an authorization-code exchange, the userinfo answer, or the organization
    /// set-up session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Renewal used to ask about the status alone, and a deactivated account is
    /// <see cref="UserStatus.Inactive"/>: offboarding changed the row and left
    /// every renewal path open. So the default is DENY, and a status added later is
    /// refused until someone decides otherwise. That is safe here and would not be
    /// safe on the sign-in path: <c>AuthenticationHelper.CheckAccountStatus</c>
    /// deliberately lets <see cref="UserStatus.Locked"/> fall through to the lock
    /// rules that follow it.
    /// </para>
    /// <para>
    /// A lock refuses renewal only when an administrator placed it. An automatic
    /// lock (timed or hard) was raised by wrong passwords, and renewing a session
    /// that already exists proves nothing about a password: refusing it protected
    /// nothing, and let anyone who knew the address sign its owner out of every
    /// device with five wrong guesses (owner decision RV-P6-6, 2026-09-27).
    /// Renewal never lifts the lock or resets the count either.
    /// </para>
    /// </remarks>
    public bool CanRenewCredentials()
    {
        if (LockKind(DateTime.UtcNow) == AccountLockKind.Admin)
        {
            return false;
        }

        return Status switch
        {
            UserStatus.Active => true,

            // Timed, hard or lapsed: the guard above already refused an
            // administrator's lock.
            UserStatus.Locked => true,

            _ => false
        };
    }

    /// <summary>
    /// The first and last name joined by one space, leaving out a part that is blank:
    /// empty for an account without a name (OI-215), never a lone space.
    /// </summary>
    public string GetFullName() => DisplayNameFor(FirstName, LastName) ?? string.Empty;

    /// <summary>
    /// The one derivation of a display name from the two name parts: the parts that
    /// are not blank, trimmed and joined by a space; null when both are blank. The
    /// store's computed FullName column puts a space between the two whatever they
    /// hold, and reading it through this rule gives the same value.
    /// </summary>
    private static string? DisplayNameFor(string? firstName, string? lastName)
    {
        var name = string.Join(' ', new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return name.Length == 0 ? null : name;
    }
}
