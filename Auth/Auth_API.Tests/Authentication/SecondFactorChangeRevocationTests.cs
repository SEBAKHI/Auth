using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The sign-out that follows a second factor given up (S24): never cancellable by the
/// caller, and never the request's failure.
/// </summary>
public class SecondFactorChangeRevocationTests
{
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();

    [Fact]
    public async Task RevokeOtherSessionsAsync_SparesTheCallersSessionAndCookie_WithoutTheCallersToken()
    {
        await _revocation.Object.RevokeOtherSessionsAsync(
            _userId, _sessionId, "plain-cookie", "Emailed second factor disabled", NullLogger.Instance);

        _revocation.Verify(r => r.RevokeCredentialsAsync(
            _userId, _sessionId, "plain-cookie", _userId, "Emailed second factor disabled", CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task RevokeOtherSessionsAsync_WhenTheRevocationFails_DoesNotFailTheChangeThatIsAlreadyCommitted()
    {
        _revocation.Setup(r => r.RevokeCredentialsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var act = () => _revocation.Object.RevokeOtherSessionsAsync(
            _userId, _sessionId, null, "Emailed second factor disabled", NullLogger.Instance);

        await act.Should().NotThrowAsync();
    }
}
