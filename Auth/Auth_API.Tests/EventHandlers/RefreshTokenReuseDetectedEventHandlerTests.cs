using Auth_API.Modules.Authentication.EventHandlers;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Application.Notifications;
using Auth.Domain.Constants;
using Auth.Domain.Events;
using ErrorOr;
using Microsoft.Extensions.Logging;

using Auth_API.Tests.Helpers;

namespace Auth_API.Tests.EventHandlers;

public class RefreshTokenReuseDetectedEventHandlerTests
{
    private readonly Mock<INotificationService> _notificationsMock = new();
    private readonly EmailSettings _emailSettings = new()
    {
        FrontendBaseUrl = "https://accounts.example.com"
    };

    private RefreshTokenReuseDetectedEventHandler CreateHandler()
    {
        _notificationsMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success);

        return new RefreshTokenReuseDetectedEventHandler(
            _notificationsMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            new Mock<ILogger<RefreshTokenReuseDetectedEventHandler>>().Object);
    }

    private static RefreshTokenReuseDetectedEvent CreateEvent(
        Guid? userId = null,
        string? ipAddress = "31.223.57.26")
        => new(
            userId ?? Guid.NewGuid(),
            "victim@example.com",
            "Jane Doe",
            ipAddress,
            new DateTime(2026, 8, 5, 9, 14, 0, DateTimeKind.Utc));

    [Fact]
    public async Task Handle_SendsTheSessionRevocationNotice()
    {
        NotificationRequest? sent = null;
        _notificationsMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result.Success);

        var handler = new RefreshTokenReuseDetectedEventHandler(
            _notificationsMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            new Mock<ILogger<RefreshTokenReuseDetectedEventHandler>>().Object);

        var evt = CreateEvent();
        await handler.Handle(evt, CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.TypeCode.Should().Be(NotificationTypeCodes.SessionsRevokedTokenReuse);
        sent.RecipientAddress.Should().Be("victim@example.com");
        // Carried so the renderer can pick the recipient's own language.
        sent.RecipientUserId.Should().Be(evt.UserId);
        sent.Variables["IpAddress"].Should().Be("31.223.57.26");
        sent.Variables["DetectedAt"].Should().Be("2026-08-05 09:14:00Z");
    }

    [Fact]
    public async Task Handle_PointsTheOnlyActionAtChangingThePassword()
    {
        // Load-bearing, not cosmetic. This notice goes out at the one moment the
        // account may already be under someone else's control, so the link must
        // lead somewhere that LOCKS THEM OUT — never anywhere that restores a
        // session — and it must carry no token, because mail scanners prefetch
        // links and would fire any one-click action before a human read it.
        NotificationRequest? sent = null;
        _notificationsMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result.Success);

        var handler = new RefreshTokenReuseDetectedEventHandler(
            _notificationsMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            new Mock<ILogger<RefreshTokenReuseDetectedEventHandler>>().Object);

        await handler.Handle(CreateEvent(), CancellationToken.None);

        var link = sent!.Variables["SecureAccountLink"] as string;
        link.Should().Be("https://accounts.example.com/forgot-password");
        link.Should().NotContain("token");
    }

    [Fact]
    public async Task Handle_RendersAMissingAddressAsADashRatherThanAnEnglishWord()
    {
        // The template is per-language: an English "Unknown" would land inside
        // an Arabic or Chinese email. A dash reads the same in all seven.
        NotificationRequest? sent = null;
        _notificationsMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(Result.Success);

        var handler = new RefreshTokenReuseDetectedEventHandler(
            _notificationsMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            new Mock<ILogger<RefreshTokenReuseDetectedEventHandler>>().Object);

        await handler.Handle(CreateEvent(ipAddress: null), CancellationToken.None);

        sent!.Variables["IpAddress"].Should().Be("—");
    }

    [Fact]
    public async Task Handle_SwallowsADeliveryFailure()
    {
        // The revocation has already committed; an unsent email cannot undo it.
        _notificationsMock
            .Setup(s => s.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Failure("Notification.SendFailed", "SMTP unavailable"));

        var handler = new RefreshTokenReuseDetectedEventHandler(
            _notificationsMock.Object,
            TestHelpers.CreateOptions(_emailSettings),
            new Mock<ILogger<RefreshTokenReuseDetectedEventHandler>>().Object);

        var act = async () => await handler.Handle(CreateEvent(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
