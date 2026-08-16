using Auth_API.Modules.AuditLog.EventHandlers;
using Auth.Domain.Entities;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.EventHandlers;

public class UserCreatedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserCreatedAuditEventHandler _handler;

    public UserCreatedAuditEventHandlerTests()
    {
        _handler = new UserCreatedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserCreatedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserCreatedEvent(Guid.NewGuid(), "test@example.com", "John", "Doe", Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserLoggedInAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserLoggedInAuditEventHandler _handler;

    public UserLoggedInAuditEventHandlerTests()
    {
        _handler = new UserLoggedInAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserLoggedInAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserLoggedInEvent(Guid.NewGuid(), "test@example.com", "127.0.0.1", "TestAgent/1.0");

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserLoggedOutAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserLoggedOutAuditEventHandler _handler;

    public UserLoggedOutAuditEventHandlerTests()
    {
        _handler = new UserLoggedOutAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserLoggedOutAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserLoggedOutEvent(Guid.NewGuid(), false);

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class PasswordChangedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly PasswordChangedAuditEventHandler _handler;

    public PasswordChangedAuditEventHandlerTests()
    {
        _handler = new PasswordChangedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<PasswordChangedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new PasswordChangedEvent(Guid.NewGuid(), Guid.NewGuid(), "user@test.com", "Jane Doe");

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_RecordsTheRotationActionNotTheFirstPasswordOne()
    {
        AuditLog? captured = null;
        _repoMock
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => captured = log);

        await _handler.Handle(new PasswordChangedEvent(Guid.NewGuid(), Guid.NewGuid(), "user@test.com", "Jane Doe"), CancellationToken.None);

        captured!.Action.Should().Be("password.changed");
    }
}

public class PasswordCreatedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly PasswordCreatedAuditEventHandler _handler;

    public PasswordCreatedAuditEventHandlerTests()
    {
        _handler = new PasswordCreatedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<PasswordCreatedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_WritesADistinctFirstPasswordAction()
    {
        // A password RESET used to write nothing at all, so the moment an external-only
        // super-admin acquired its first credential left no record. This is that record,
        // and it must not be mistakable for a rotation.
        AuditLog? captured = null;
        _repoMock
            .Setup(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => captured = log);

        var userId = Guid.NewGuid();
        var setBy = Guid.NewGuid();

        await _handler.Handle(
            new PasswordCreatedEvent(userId, setBy, "user@test.com", "Jane Doe"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ActionType.Should().Be("Security");
        captured.Action.Should().Be("password.created");
        captured.Action.Should().NotBe("password.changed");
        captured.UserId.Should().Be(setBy);
        captured.EntityType.Should().Be("User");
        captured.EntityId.Should().Be(userId);
        captured.IsSuccess.Should().BeTrue();
    }
}

public class RoleAssignedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly RoleAssignedAuditEventHandler _handler;

    public RoleAssignedAuditEventHandlerTests()
    {
        _handler = new RoleAssignedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<RoleAssignedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new RoleAssignedEvent(Guid.NewGuid(), Guid.NewGuid(), "Admin", Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserLockedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserLockedAuditEventHandler _handler;

    public UserLockedAuditEventHandlerTests()
    {
        _handler = new UserLockedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserLockedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserLockedEvent(Guid.NewGuid(), DateTime.UtcNow.AddHours(1), Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserUnlockedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserUnlockedAuditEventHandler _handler;

    public UserUnlockedAuditEventHandlerTests()
    {
        _handler = new UserUnlockedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserUnlockedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserUnlockedEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserDeletedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserDeletedAuditEventHandler _handler;

    public UserDeletedAuditEventHandlerTests()
    {
        _handler = new UserDeletedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserDeletedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new UserDeletedEvent(Guid.NewGuid(), "deleted@example.com", Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class UserHardDeletedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly UserHardDeletedAuditEventHandler _handler;

    public UserHardDeletedAuditEventHandlerTests()
    {
        _handler = new UserHardDeletedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<UserHardDeletedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesTombstoneAttributedToAdministrator()
    {
        var purgedUserId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var evt = new UserHardDeletedEvent(purgedUserId, "purged@example.com", adminId);

        await _handler.Handle(evt, CancellationToken.None);

        // The purge removed the account's own audit rows, so the tombstone must
        // reference only the administrator — never the deleted user id in UserId.
        _repoMock.Verify(r => r.CreateAsync(
            It.Is<AuditLog>(log =>
                log.Action == "user.harddeleted" &&
                log.UserId == adminId &&
                log.EntityId == purgedUserId),
            It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class TwoFactorEnabledAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly TwoFactorEnabledAuditEventHandler _handler;

    public TwoFactorEnabledAuditEventHandlerTests()
    {
        _handler = new TwoFactorEnabledAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<TwoFactorEnabledAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new TwoFactorEnabledEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class TwoFactorDisabledAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly TwoFactorDisabledAuditEventHandler _handler;

    public TwoFactorDisabledAuditEventHandlerTests()
    {
        _handler = new TwoFactorDisabledAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<TwoFactorDisabledAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new TwoFactorDisabledEvent(Guid.NewGuid(), Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class ApiKeyCreatedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly ApiKeyCreatedAuditEventHandler _handler;

    public ApiKeyCreatedAuditEventHandlerTests()
    {
        _handler = new ApiKeyCreatedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<ApiKeyCreatedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new ApiKeyCreatedEvent(Guid.NewGuid(), Guid.NewGuid(), "TestKey", Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class ApiKeyRevokedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly ApiKeyRevokedAuditEventHandler _handler;

    public ApiKeyRevokedAuditEventHandlerTests()
    {
        _handler = new ApiKeyRevokedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<ApiKeyRevokedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var evt = new ApiKeyRevokedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(r => r.CreateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}

public class PlatformSettingsUpdatedAuditEventHandlerTests
{
    private readonly Mock<IAuditLogRepository> _repoMock = new();
    private readonly PlatformSettingsUpdatedAuditEventHandler _handler;

    public PlatformSettingsUpdatedAuditEventHandlerTests()
    {
        _handler = new PlatformSettingsUpdatedAuditEventHandler(
            _repoMock.Object,
            new Mock<ILogger<PlatformSettingsUpdatedAuditEventHandler>>().Object);
    }

    [Fact]
    public async Task Handle_CreatesAuditLogEntry()
    {
        var updatedBy = Guid.NewGuid();
        var evt = new PlatformSettingsUpdatedEvent(
            Guid.NewGuid(), "Auth Console", "Sebakhi Console", null, "logo.webp", null, "logo-dark.webp", null, "favicon.webp", updatedBy);

        await _handler.Handle(evt, CancellationToken.None);

        _repoMock.Verify(
            r => r.CreateAsync(
                It.Is<AuditLog>(log =>
                    log.Action == "platform-settings.updated" &&
                    log.EntityType == "PlatformSettings" &&
                    log.UserId == updatedBy &&
                    log.NewValues!.Contains("\"logoUrlDark\":\"logo-dark.webp\"")),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }
}
