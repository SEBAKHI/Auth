using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Events;

namespace Auth_API.Tests.Domain.Entities;

/// <summary>
/// Unit tests for the <see cref="User"/> aggregate root entity.
/// Covers factory methods, state mutations, domain event raising, and query methods.
/// </summary>
public class UserTests
{
    private readonly Guid _createdBy = Guid.NewGuid();

    #region Helper Methods

    private User CreateDefaultUser(
        UserStatus status = UserStatus.Active,
        bool emailConfirmed = true,
        int failedLoginAttempts = 0,
        DateTime? lockoutEnd = null,
        bool twoFactorEnabled = false,
        string? twoFactorSecret = null,
        bool mustChangePassword = false)
    {
        return new User(
            id: Guid.NewGuid(),
            email: "test@example.com",
            normalizedEmail: "TEST@EXAMPLE.COM",
            passwordHash: "hashed_password",
            firstName: "John",
            lastName: "Doe",
            displayName: "John Doe",
            phoneNumber: null,
            status: status,
            emailConfirmed: emailConfirmed,
            phoneConfirmed: false,
            twoFactorEnabled: twoFactorEnabled,
            twoFactorSecret: twoFactorSecret,
            failedLoginAttempts: failedLoginAttempts,
            lockoutEnd: lockoutEnd,
            lastLoginAt: null,
            passwordChangedAt: DateTime.UtcNow,
            mustChangePassword: mustChangePassword,
            preferredLanguage: "en",
            timeZone: "UTC",
            metadata: null,
            isSystemUser: false,
            createdAt: DateTime.UtcNow,
            createdBy: _createdBy,
            modifiedAt: null,
            modifiedBy: null);
    }

    #endregion

    #region Create Tests

    [Fact]
    public void Create_ValidParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        var email = "user@example.com";
        var passwordHash = "argon2id_hash";
        var firstName = "Alice";
        var lastName = "Smith";
        var createdBy = Guid.NewGuid();

        // Act
        var user = User.Create(email, passwordHash, firstName, lastName, createdBy);

        // Assert
        user.Email.Value.Should().Be(email);
        user.PasswordHash.Should().Be(passwordHash);
        user.FirstName.Should().Be(firstName);
        user.LastName.Should().Be(lastName);
        user.DisplayName.Should().Be("Alice Smith");
        user.Status.Should().Be(UserStatus.Active);
        user.EmailConfirmed.Should().BeFalse();
        user.TwoFactorEnabled.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        user.MustChangePassword.Should().BeFalse();
        user.IsSystemUser.Should().BeFalse();
        user.PreferredLanguage.Should().Be("en");
        user.TimeZone.Should().Be("UTC");
        user.Theme.Should().Be("system");
        user.PasswordChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_ValidParameters_RaisesUserCreatedEvent()
    {
        // Arrange
        var createdBy = Guid.NewGuid();

        // Act
        var user = User.Create("user@example.com", "hash", "Alice", "Smith", createdBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<UserCreatedEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.FirstName.Should().Be("Alice");
        domainEvent.LastName.Should().Be("Smith");
        domainEvent.CreatedBy.Should().Be(createdBy);
    }

    [Fact]
    public void Create_WithCustomDisplayName_UsesProvidedDisplayName()
    {
        // Arrange & Act
        var user = User.Create("user@example.com", "hash", "Alice", "Smith", Guid.NewGuid(), displayName: "A. Smith");

        // Assert
        user.DisplayName.Should().Be("A. Smith");
    }

    [Fact]
    public void Create_WithCustomLanguageAndTimeZone_SetsValues()
    {
        // Arrange & Act
        var user = User.Create("user@example.com", "hash", "Alice", "Smith", Guid.NewGuid(),
            preferredLanguage: "ar", timeZone: "Asia/Riyadh");

        // Assert
        user.PreferredLanguage.Should().Be("ar");
        user.TimeZone.Should().Be("Asia/Riyadh");
    }

    [Fact]
    public void Create_WithCustomTheme_SetsValue()
    {
        // Arrange & Act
        var user = User.Create("user@example.com", "hash", "Alice", "Smith", Guid.NewGuid(),
            theme: "dark");

        // Assert
        user.Theme.Should().Be("dark");
    }

    #endregion

    #region CreateFromExternalProvider Tests

    [Fact]
    public void CreateFromExternalProvider_ValidParameters_SetsPasswordHashToNull()
    {
        // Arrange & Act
        var user = User.CreateFromExternalProvider("user@example.com", "Alice", "Smith", Guid.NewGuid());

        // Assert
        user.PasswordHash.Should().BeNull();
    }

    [Fact]
    public void CreateFromExternalProvider_ValidParameters_SetsEmailConfirmedTrue()
    {
        // Arrange & Act
        var user = User.CreateFromExternalProvider("user@example.com", "Alice", "Smith", Guid.NewGuid());

        // Assert
        user.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public void CreateFromExternalProvider_DefaultParameters_DefaultsThemeToSystem()
    {
        // Arrange & Act
        var user = User.CreateFromExternalProvider("user@example.com", "Alice", "Smith", Guid.NewGuid());

        // Assert
        user.Theme.Should().Be("system");
    }

    [Fact]
    public void CreateFromExternalProvider_ValidParameters_RaisesUserCreatedEvent()
    {
        // Arrange
        var createdBy = Guid.NewGuid();

        // Act
        var user = User.CreateFromExternalProvider("user@example.com", "Alice", "Smith", createdBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        user.DomainEvents[0].Should().BeOfType<UserCreatedEvent>();
    }

    #endregion

    #region UpdateProfile Tests

    [Fact]
    public void UpdateProfile_ValidParameters_UpdatesAllProfileFields()
    {
        // Arrange
        var user = CreateDefaultUser();
        var modifiedBy = Guid.NewGuid();

        // Act
        user.UpdateProfile("Jane", "Doe", "J. Doe", "+1234567890", "fr", "Europe/Paris", "dark", modifiedBy);

        // Assert
        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Doe");
        user.DisplayName.Should().Be("J. Doe");
        user.PreferredLanguage.Should().Be("fr");
        user.TimeZone.Should().Be("Europe/Paris");
        user.Theme.Should().Be("dark");
        user.ModifiedBy.Should().Be(modifiedBy);
        user.ModifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdateProfile_NullDisplayName_SetsDisplayNameToNull()
    {
        // Arrange
        var user = CreateDefaultUser();

        // Act
        user.UpdateProfile("Jane", "Doe", null, null, null, null, null, Guid.NewGuid());

        // Assert
        user.DisplayName.Should().BeNull();
    }

    #endregion

    #region ChangePassword Tests

    [Fact]
    public void ChangePassword_ValidHash_UpdatesPasswordHash()
    {
        // Arrange
        var user = CreateDefaultUser();
        var newHash = "new_argon2id_hash";
        var modifiedBy = Guid.NewGuid();

        // Act
        user.ChangePassword(newHash, modifiedBy);

        // Assert
        user.PasswordHash.Should().Be(newHash);
    }

    [Fact]
    public void ChangePassword_Always_SetsPasswordChangedAtAndClearsMustChange()
    {
        // Arrange
        var user = CreateDefaultUser(mustChangePassword: true);

        // Act
        user.ChangePassword("new_hash", Guid.NewGuid());

        // Assert
        user.PasswordChangedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        user.MustChangePassword.Should().BeFalse();
    }

    [Fact]
    public void ChangePassword_Always_RaisesPasswordChangedEvent()
    {
        // Arrange
        var user = CreateDefaultUser();
        var modifiedBy = Guid.NewGuid();

        // Act
        user.ChangePassword("new_hash", modifiedBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<PasswordChangedEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.ChangedBy.Should().Be(modifiedBy);
    }

    #endregion

    #region RecordSuccessfulLogin Tests

    [Fact]
    public void RecordSuccessfulLogin_Always_SetsLastLoginAtAndResetsFailedAttempts()
    {
        // Arrange
        var user = CreateDefaultUser(failedLoginAttempts: 3);

        // Act
        user.RecordSuccessfulLogin("192.168.1.1", "Chrome");

        // Assert
        user.LastLoginAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public void RecordSuccessfulLogin_Always_RaisesUserLoggedInEvent()
    {
        // Arrange
        var user = CreateDefaultUser();
        var ipAddress = "10.0.0.1";
        var userAgent = "Mozilla/5.0";

        // Act
        user.RecordSuccessfulLogin(ipAddress, userAgent);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<UserLoggedInEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.IpAddress.Should().Be(ipAddress);
        domainEvent.UserAgent.Should().Be(userAgent);
    }

    [Fact]
    public void RecordSuccessfulLogin_WithNullParameters_RaisesEventWithNulls()
    {
        // Arrange
        var user = CreateDefaultUser();

        // Act
        user.RecordSuccessfulLogin();

        // Assert
        var domainEvent = user.DomainEvents[0].Should().BeOfType<UserLoggedInEvent>().Subject;
        domainEvent.IpAddress.Should().BeNull();
        domainEvent.UserAgent.Should().BeNull();
    }

    #endregion

    #region RecordFailedLogin Tests

    [Fact]
    public void RecordFailedLogin_BelowThreshold_IncrementsAttemptsOnly()
    {
        // Arrange
        var user = CreateDefaultUser(failedLoginAttempts: 0);

        // Act
        user.RecordFailedLogin(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        // Assert
        user.FailedLoginAttempts.Should().Be(1);
        user.Status.Should().Be(UserStatus.Active);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public void RecordFailedLogin_ReachesMaxAttempts_LocksAccount()
    {
        // Arrange
        var user = CreateDefaultUser(failedLoginAttempts: 4);
        var lockoutDuration = TimeSpan.FromMinutes(30);

        // Act
        user.RecordFailedLogin(maxAttempts: 5, lockoutDuration: lockoutDuration);

        // Assert
        user.FailedLoginAttempts.Should().Be(5);
        user.Status.Should().Be(UserStatus.Locked);
        user.LockoutEnd.Should().BeCloseTo(DateTime.UtcNow.Add(lockoutDuration), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RecordFailedLogin_ExceedsMaxAttempts_RemainsLocked()
    {
        // Arrange
        var user = CreateDefaultUser(failedLoginAttempts: 6);

        // Act
        user.RecordFailedLogin(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

        // Assert
        user.FailedLoginAttempts.Should().Be(7);
        user.Status.Should().Be(UserStatus.Locked);
    }

    #endregion

    #region IsLockedOut Tests

    [Fact]
    public void IsLockedOut_StatusLockedWithFutureLockoutEnd_ReturnsTrue()
    {
        // Arrange
        var user = CreateDefaultUser(
            status: UserStatus.Locked,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddHours(1));

        // Act
        var result = user.IsLockedOut();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_StatusLockedWithNullLockoutEnd_ReturnsTrue()
    {
        // Arrange
        var user = CreateDefaultUser(
            status: UserStatus.Locked,
            failedLoginAttempts: 5,
            lockoutEnd: null);

        // Act
        var result = user.IsLockedOut();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsLockedOut_StatusLockedWithPastLockoutEnd_ReturnsFalse()
    {
        // Arrange
        var user = CreateDefaultUser(
            status: UserStatus.Locked,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddHours(-1));

        // Act
        var result = user.IsLockedOut();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsLockedOut_StatusActive_ReturnsFalse()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Active);

        // Act
        var result = user.IsLockedOut();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Unlock Tests

    [Fact]
    public void Unlock_LockedUser_SetsStatusActiveAndResetsLockout()
    {
        // Arrange
        var user = CreateDefaultUser(
            status: UserStatus.Locked,
            failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddHours(1));
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Unlock(modifiedBy);

        // Assert
        user.Status.Should().Be(UserStatus.Active);
        user.FailedLoginAttempts.Should().Be(0);
        user.LockoutEnd.Should().BeNull();
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void Unlock_LockedUser_RaisesUserUnlockedEvent()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Locked, failedLoginAttempts: 5);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Unlock(modifiedBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<UserUnlockedEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.UnlockedBy.Should().Be(modifiedBy);
    }

    #endregion

    #region Lock Tests

    [Fact]
    public void Lock_ActiveUser_SetsStatusLockedAndLockoutEnd()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Active);
        var lockoutEnd = DateTime.UtcNow.AddHours(2);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Lock(lockoutEnd, modifiedBy);

        // Assert
        user.Status.Should().Be(UserStatus.Locked);
        user.LockoutEnd.Should().Be(lockoutEnd);
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void Lock_WithNullLockoutEnd_SetsIndefiniteLock()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Active);

        // Act
        user.Lock(null, Guid.NewGuid());

        // Assert
        user.Status.Should().Be(UserStatus.Locked);
        user.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public void Lock_Always_RaisesUserLockedEvent()
    {
        // Arrange
        var user = CreateDefaultUser();
        var lockoutEnd = DateTime.UtcNow.AddHours(1);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Lock(lockoutEnd, modifiedBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<UserLockedEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.LockoutEnd.Should().Be(lockoutEnd);
        domainEvent.LockedBy.Should().Be(modifiedBy);
    }

    #endregion

    #region Activate / Deactivate Tests

    [Fact]
    public void Activate_InactiveUser_SetsStatusActive()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Inactive);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Activate(modifiedBy);

        // Assert
        user.Status.Should().Be(UserStatus.Active);
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void Deactivate_ActiveUser_SetsStatusInactive()
    {
        // Arrange
        var user = CreateDefaultUser(status: UserStatus.Active);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.Deactivate(modifiedBy);

        // Assert
        user.Status.Should().Be(UserStatus.Inactive);
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    #endregion

    #region ConfirmEmail Tests

    [Fact]
    public void ConfirmEmail_UnconfirmedEmail_SetsEmailConfirmedTrue()
    {
        // Arrange
        var user = CreateDefaultUser(emailConfirmed: false);
        var modifiedBy = Guid.NewGuid();

        // Act
        user.ConfirmEmail(modifiedBy);

        // Assert
        user.EmailConfirmed.Should().BeTrue();
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    #endregion

    #region EnableTwoFactor / DisableTwoFactor Tests

    [Fact]
    public void EnableTwoFactor_ValidSecret_SetsTwoFactorEnabledAndSecret()
    {
        // Arrange
        var user = CreateDefaultUser();
        var secret = "JBSWY3DPEHPK3PXP";
        var modifiedBy = Guid.NewGuid();

        // Act
        user.EnableTwoFactor(secret, modifiedBy);

        // Assert
        user.TwoFactorEnabled.Should().BeTrue();
        user.TwoFactorSecret.Should().Be(secret);
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void EnableTwoFactor_Always_RaisesTwoFactorEnabledEvent()
    {
        // Arrange
        var user = CreateDefaultUser();
        var modifiedBy = Guid.NewGuid();

        // Act
        user.EnableTwoFactor("secret", modifiedBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<TwoFactorEnabledEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.EnabledBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void DisableTwoFactor_WhenEnabled_ClearsTwoFactorAndSecret()
    {
        // Arrange
        var user = CreateDefaultUser(twoFactorEnabled: true, twoFactorSecret: "existing_secret");
        var modifiedBy = Guid.NewGuid();

        // Act
        user.DisableTwoFactor(modifiedBy);

        // Assert
        user.TwoFactorEnabled.Should().BeFalse();
        user.TwoFactorSecret.Should().BeNull();
        user.ModifiedBy.Should().Be(modifiedBy);
    }

    [Fact]
    public void DisableTwoFactor_Always_RaisesTwoFactorDisabledEvent()
    {
        // Arrange
        var user = CreateDefaultUser(twoFactorEnabled: true, twoFactorSecret: "secret");
        var modifiedBy = Guid.NewGuid();

        // Act
        user.DisableTwoFactor(modifiedBy);

        // Assert
        user.DomainEvents.Should().ContainSingle();
        var domainEvent = user.DomainEvents[0].Should().BeOfType<TwoFactorDisabledEvent>().Subject;
        domainEvent.UserId.Should().Be(user.Id);
        domainEvent.DisabledBy.Should().Be(modifiedBy);
    }

    #endregion

    #region GetFullName Tests

    [Fact]
    public void GetFullName_Always_ReturnsFirstNameSpaceLastName()
    {
        // Arrange
        var user = CreateDefaultUser();

        // Act
        var fullName = user.GetFullName();

        // Assert
        fullName.Should().Be("John Doe");
    }

    [Fact]
    public void GetFullName_FromFactoryMethod_ReturnsCorrectFormat()
    {
        // Arrange
        var user = User.Create("test@example.com", "hash", "Alice", "Smith", Guid.NewGuid());

        // Act
        var fullName = user.GetFullName();

        // Assert
        fullName.Should().Be("Alice Smith");
    }

    #endregion
}
