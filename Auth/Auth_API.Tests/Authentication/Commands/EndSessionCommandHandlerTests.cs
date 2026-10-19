using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.EndSession;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for the two halves of RP-initiated logout: the GET that decides
/// where the browser goes, and the POST that actually ends the session.
/// </summary>
public class EndSessionCommandHandlerTests
{
    private const string ClientId = "CRM";
    private const string AccountsBase = "https://accounts.example.com";

    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly Mock<IRefreshTokenKeyService> _tokenKeys = new();

    private readonly EndSessionCommandHandler _handler;
    private readonly ConfirmEndSessionCommandHandler _confirm;

    public EndSessionCommandHandlerTests()
    {
        _tokenKeys.Setup(s => s.ComputeTokenHash(It.IsAny<string>()))
            .Returns((string t) => $"hash:{t}");

        var settings = new IdentityProviderSettings { AccountsBaseUrl = AccountsBase };
        _handler = new EndSessionCommandHandler(
            _applications.Object,
            TestIdpSessions.Reader(_idpSessions, _tokenKeys.Object, settings),
            TestHelpers.CreateOptions(settings),
            new Mock<ILogger<EndSessionCommandHandler>>().Object);

        _confirm = new ConfirmEndSessionCommandHandler(
            new Auth.Infrastructure.Authentication.CredentialRevocationService(
                new Mock<IUserSessionRepository>().Object, new InMemoryRefreshTokenRepository(), _idpSessions.Object,
                new Mock<ITokenBlacklistService>().Object, _tokenKeys.Object, TestHelpers.CreateOptions(new JwtSettings()),
                new Mock<IIdpSessionReader>().Object, TestHelpers.CreateOptions(settings), new Mock<MediatR.IPublisher>().Object,
                TimeProvider.System, new Mock<ILogger<Auth.Infrastructure.Authentication.CredentialRevocationService>>().Object),
            new Mock<ILogger<ConfirmEndSessionCommandHandler>>().Object);
    }

    private void SetupApplication(bool isActive = true) =>
        _applications
            .Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateApplication(code: ClientId, isActive: isActive));

    private IdpSession SetupLiveSession(string token = "sso-token")
    {
        // Signed in an hour ago and used moments ago: a browser session unused
        // for the idle limit is no longer live (OI-154).
        var session = new IdpSession(
            Guid.NewGuid(), Guid.NewGuid(), $"hash:{token}",
            DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddDays(7), null, null, null,
            lastActivityAt: DateTime.UtcNow);

        _idpSessions
            .Setup(r => r.GetByTokenHashAsync($"hash:{token}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        return session;
    }

    // --- the GET: decide, never act ---

    [Fact]
    public async Task Handle_WithLiveSession_SendsTheBrowserToConfirmRatherThanEndingAnything()
    {
        SetupApplication();
        SetupLiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        // Asserted through the destination rather than a flag beside it: the URL
        // IS the decision, and a second field restating it could disagree.
        // Pinned exactly: a parameter added to every accounts URL must show here.
        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/logout?client_id=CRM&state=xyz");

        // Nothing was revoked on a GET. Acting here would let any page sign our
        // users out by loading this URL in an image tag.
        _idpSessions.Verify(
            r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithoutAClientId_IsAcceptedBecauseTheSpecMakesItOptional()
    {
        // Requiring it would have recreated the exact fault this endpoint exists
        // to fix: an address the discovery document advertises that a conformant
        // client cannot use.
        SetupLiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(null, null, "sso-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/logout");
        _applications.Verify(
            r => r.GetByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithAClientIdThatNamesNobody_IsRefusedWithoutRedirecting()
    {
        _applications
            .Setup(r => r.GetByCodeAsync("GHOST", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auth.Domain.Entities.Application?)null);

        var result = await _handler.Handle(
            new EndSessionCommand("GHOST", null, "sso-token"), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithAnInactiveClient_IsRefused()
    {
        SetupApplication(isActive: false);

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, null, "sso-token"), CancellationToken.None);

        result.IsError.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-token-naming-no-session")]
    public async Task Handle_WithNothingToEnd_GoesStraightToSignedOut(string? token)
    {
        // Not an error. A second click, a refresh, or a tab left open past the
        // session lifetime all arrive here already in the state they asked for.
        SetupApplication();
        _idpSessions
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession?)null);

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, null, token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.RedirectUrl.Should().StartWith($"{AccountsBase}/signed-out");
    }

    [Fact]
    public async Task Handle_WithNothingToEndAndAState_IsExactlyTheSignedOutPageWithClientAndState()
    {
        SetupApplication();
        _idpSessions
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdpSession?)null);

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "xyz", "sso-token"), CancellationToken.None);

        result.Value.RedirectUrl.Should().Be($"{AccountsBase}/signed-out?client_id=CRM&state=xyz");
    }

    [Fact]
    public async Task Handle_OnlyEverNamesOurOwnOrigin()
    {
        // With no post_logout_redirect_uri, no caller-supplied value reaches the
        // destination: an address in state stays a query value on our own page.
        // The registered return is pinned in EndSessionPostLogoutRedirectTests.
        SetupApplication();
        SetupLiveSession();

        var result = await _handler.Handle(
            new EndSessionCommand(ClientId, "https://evil.example.com", "sso-token"),
            CancellationToken.None);

        result.Value.RedirectUrl.Should().StartWith(AccountsBase);
    }

    // --- the POST: act, once the user has said so ---

    [Fact]
    public async Task Confirm_RevokesTheLiveSession()
    {
        var session = SetupLiveSession();

        var result = await _confirm.Handle(
            new ConfirmEndSessionCommand("sso-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        session.IsRevoked.Should().BeTrue();
        _idpSessions.Verify(r => r.UpdateAsync(session, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Confirm_WithNoCookie_SucceedsWithoutTouchingAnything(string? token)
    {
        // Ending a session that is already ended is the outcome the caller wanted.
        // An error here would turn a double click into a failure to interpret.
        var result = await _confirm.Handle(
            new ConfirmEndSessionCommand(token), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _idpSessions.Verify(
            r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_OnAnAlreadyRevokedSession_IsIdempotent()
    {
        var revoked = new IdpSession(
            Guid.NewGuid(), Guid.NewGuid(), "hash:sso-token",
            DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddDays(7), DateTime.UtcNow.AddMinutes(-5), null, null);

        _idpSessions
            .Setup(r => r.GetByTokenHashAsync("hash:sso-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revoked);

        var result = await _confirm.Handle(
            new ConfirmEndSessionCommand("sso-token"), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _idpSessions.Verify(
            r => r.UpdateAsync(It.IsAny<IdpSession>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
