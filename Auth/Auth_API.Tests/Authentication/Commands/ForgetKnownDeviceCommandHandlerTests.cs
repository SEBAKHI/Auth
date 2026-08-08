using Auth.Application.Features.Authentication.ForgetKnownDevice;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Forgetting a browser is the one place the device→session cascade is allowed
/// to run, so these cover both that it happens and that it stops short of the
/// caller's own browser.
/// </summary>
public class ForgetKnownDeviceCommandHandlerTests
{
    private const string DeviceHash = "abc123";

    private readonly Mock<IUserKnownDeviceRepository> _devicesMock = new();
    private readonly Mock<IUserSessionRepository> _sessionsMock = new();
    private readonly Mock<ICredentialRevocationService> _revocationMock = new();
    private readonly ForgetKnownDeviceCommandHandler _handler;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public ForgetKnownDeviceCommandHandlerTests()
    {
        _handler = new ForgetKnownDeviceCommandHandler(
            _devicesMock.Object,
            _sessionsMock.Object,
            _revocationMock.Object,
            new Mock<ILogger<ForgetKnownDeviceCommandHandler>>().Object);

        _sessionsMock
            .Setup(r => r.GetActiveByDeviceHashAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void GivenDeviceExists() =>
        _devicesMock
            .Setup(r => r.GetByIdAsync(_userId, _deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserKnownDevice(
                _deviceId, _userId, DeviceHash, "Chrome on Windows",
                DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, null));

    private Task<ErrorOr.ErrorOr<int>> Forget(Guid? currentSessionId = null) =>
        _handler.Handle(
            new ForgetKnownDeviceCommand(_userId, _deviceId, currentSessionId),
            CancellationToken.None);

    [Fact]
    public async Task UnknownDevice_ReturnsNotFound()
    {
        _devicesMock
            .Setup(r => r.GetByIdAsync(_userId, _deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserKnownDevice?)null);

        var result = await Forget();

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(DeviceErrors.NotFound);
    }

    [Fact]
    public async Task AnotherUsersDevice_ReadsAsNotFoundRatherThanForbidden()
    {
        // The lookup is scoped to the user in SQL, so someone else's id comes
        // back as null. Answering "forbidden" would confirm the id is real and
        // turn the endpoint into an existence oracle.
        _devicesMock
            .Setup(r => r.GetByIdAsync(_userId, _deviceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserKnownDevice?)null);

        var result = await Forget();

        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.NotFound);
        _devicesMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TheBrowserHoldingTheCurrentSession_IsRefused()
    {
        // A control labelled "forget" must not sign the user out.
        var currentSessionId = Guid.NewGuid();
        GivenDeviceExists();
        _sessionsMock
            .Setup(r => r.GetActiveByDeviceHashAsync(_userId, DeviceHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateUserSession(id: currentSessionId, userId: _userId)]);

        var result = await Forget(currentSessionId);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(DeviceErrors.CannotForgetCurrent);
        _devicesMock.Verify(
            r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _revocationMock.Verify(
            r => r.TerminateSessionsByDeviceAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnotherBrowser_EndsItsSessionsAndDeletesTheRecord()
    {
        GivenDeviceExists();
        _sessionsMock
            .Setup(r => r.GetActiveByDeviceHashAsync(_userId, DeviceHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestHelpers.CreateUserSession(userId: _userId)]);
        _revocationMock
            .Setup(r => r.TerminateSessionsByDeviceAsync(
                _userId, DeviceHash, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await Forget(Guid.NewGuid());

        result.IsError.Should().BeFalse();
        result.Value.Should().Be(2);
        _revocationMock.Verify(
            r => r.TerminateSessionsByDeviceAsync(
                _userId, DeviceHash, "device_forgotten", It.IsAny<CancellationToken>()),
            Times.Once);
        _devicesMock.Verify(
            r => r.DeleteAsync(_userId, _deviceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SessionsAreEndedBeforeTheRecordIsDeleted()
    {
        // Order matters for what the user is told. If the delete landed first and
        // the termination then failed, they would have been shown "browser
        // removed" while it stayed signed in.
        GivenDeviceExists();
        var sequence = new List<string>();
        _revocationMock
            .Setup(r => r.TerminateSessionsByDeviceAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("terminate"))
            .ReturnsAsync(1);
        _devicesMock
            .Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("delete"))
            .ReturnsAsync(true);

        await Forget();

        sequence.Should().Equal("terminate", "delete");
    }

    [Fact]
    public async Task WithNoCurrentSession_SkipsTheGuardAndStillForgets()
    {
        // An unauthenticated-shaped call cannot happen through the controller,
        // but the guard must not be what makes the operation work.
        GivenDeviceExists();
        _revocationMock
            .Setup(r => r.TerminateSessionsByDeviceAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await Forget(currentSessionId: null);

        result.IsError.Should().BeFalse();
        _devicesMock.Verify(
            r => r.DeleteAsync(_userId, _deviceId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
