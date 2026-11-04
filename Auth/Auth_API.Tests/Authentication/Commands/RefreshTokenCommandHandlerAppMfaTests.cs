using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using ApplicationEntity = Auth.Domain.Entities.Application;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S23, AM6 (D-T1 C, D-T10 C): the refresh of an application that requires two-step
/// verification. Under Enforce a session that never proved two factors ends at its next
/// refresh (this session only), a token without a session is revoked alone, and the read
/// is the decision, so its failure fails the refresh (ARR-0751). The row is read whatever
/// its end says (ARR-0791). Under Monitor nothing is refused, and a platform token stays
/// on M5. Neither path writes amr or auth_time into the application's token (ARR-0777 (a)).
/// </summary>
/// <remarks>
/// Deliberate break: catch the read failure under Enforce and read Unknown instead; the
/// "read failure propagates" test turns red.
/// </remarks>
public class RefreshTokenCommandHandlerAppMfaTests
{
    private const string Presented = "presented-refresh-token";
    private static readonly AuthenticationMethods PasswordOnly = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods PasswordAndTotp = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _tokens = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly FakeCredentialHasher _hashes = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly CapturingLogger<RefreshTokenCommandHandler> _log = new();
    private readonly IdentityProviderSettings _settings = new()
    {
        ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Enforce)
    };

    private readonly RefreshTokenCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionId = Guid.NewGuid();
    private ApplicationEntity _application = null!;

    public RefreshTokenCommandHandlerAppMfaTests()
    {
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId));
        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _tokens.Setup(r => r.TryRotateAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<(Guid, string)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-jwt");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns("new-refresh-token");

        _handler = new RefreshTokenCommandHandler(
            _users.Object, _tokens.Object, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(),
            _applications.Object, _access.Object, _jwt.Object, _hashes, _sessions.Object, _revocation.Object,
            Mock.Of<IIdpSessionReader>(), Mock.Of<IClientAuthenticator>(), Mock.Of<IPublisher>(),
            TestHelpers.CreateOptions(new JwtSettings { AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 7, RotateRefreshTokens = true }),
            TestHelpers.CreateOptions(_settings), _log);
    }

    private RefreshTokenEntity Token(bool withSession = true, bool requireTwoFactor = true, bool platform = false)
    {
        _application = new ApplicationEntity(
            Guid.NewGuid(), "CRM", "CRM", null, "https://crm.example.com", null, null,
            isActive: true, allowSelfRegistration: false, requireTwoFactor: requireTwoFactor,
            requireEmailVerification: false, sessionTimeoutMinutes: 60, maxConcurrentSessions: 5,
            DateTime.UtcNow, Guid.NewGuid(), null, null, ApplicationAccessMode.Everyone);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);

        var token = TestHelpers.CreateRefreshToken(
            userId: _userId,
            applicationId: platform ? null : _application.Id,
            expiresAt: DateTime.UtcNow.AddDays(7),
            sessionId: withSession ? _sessionId : null);
        _hashes.Map(Presented, "presented-hash");
        _tokens.Setup(r => r.GetByTokenHashAsync(FakeCredentialHasher.CandidatesOf("presented-hash"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);
        return token;
    }

    private void SessionRow(AuthenticationMethods methods, bool ended = false) =>
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: _sessionId, userId: _userId, isActive: !ended,
                terminatedAt: ended ? DateTime.UtcNow.AddHours(-1) : null, methods: methods));

    private Task<ErrorOr.ErrorOr<Auth.Application.DTOs.TokenResponse>> Refresh() =>
        _handler.Handle(new RefreshTokenCommand(Presented, "127.0.0.1", "agent"), CancellationToken.None);

    private void VerifyNoMint() =>
        _jwt.Verify(s => s.GenerateAccessToken(
            It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(), It.IsAny<IEnumerable<(Guid, string)>?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()), Times.Never);

    [Fact]
    public async Task Handle_EnforceSessionWithoutTwoFactors_EndsThatSession_AndReturnsTwoFactorRequired()
    {
        Token();
        SessionRow(PasswordOnly);

        var result = await Refresh();

        result.FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
        _revocation.Verify(r => r.TerminateSessionAsync(
            _sessionId, null, TokenRevocationReasons.ApplicationMfaRequired, It.IsAny<CancellationToken>()), Times.Once);
        _revocation.VerifyNoOtherCalls();
        _tokens.Verify(r => r.RevokeAllForUserAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _log.Entries.Should().ContainSingle(entry => entry.Values.Contains(AppMfaSignalCodes.Refused));
        VerifyNoMint();
    }

    [Fact]
    public async Task Handle_EnforceTokenWithoutASession_RevokesThatToken_AndReturnsTwoFactorRequired()
    {
        var token = Token(withSession: false);

        var result = await Refresh();

        result.FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
        token.ReasonRevoked.Should().Be(TokenRevocationReasons.ApplicationMfaRequired);
        _tokens.Verify(r => r.UpdateAsync(token, It.IsAny<CancellationToken>()), Times.Once);
        _revocation.Verify(r => r.TerminateSessionAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EnforceMissingRow_IsUnknown_AndReturnsTwoFactorRequired()
    {
        Token();
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>())).ReturnsAsync((UserSession?)null);

        (await Refresh()).FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
    }

    [Fact]
    public async Task Handle_EnforceReadFailure_Propagates_AndEndsNothing()
    {
        // Fail closed (ARR-0751): the read is the decision; the next refresh decides again.
        Token();
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database"));

        var act = Refresh;

        await act.Should().ThrowAsync<TimeoutException>();
        _revocation.VerifyNoOtherCalls();
        VerifyNoMint();
    }

    [Fact]
    public async Task Handle_EnforceEndedRowThatProvedTwoFactors_IsReadAndRenews()
    {
        // ARR-0791 (1): the row is read whatever its end says.
        Token();
        SessionRow(PasswordAndTotp, ended: true);

        var result = await Refresh();

        result.IsError.Should().BeFalse();
        _revocation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_EnforceRowOfAnotherUser_IsUnknown()
    {
        Token();
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: _sessionId, userId: Guid.NewGuid(), methods: PasswordAndTotp));

        (await Refresh()).FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
    }

    [Fact]
    public async Task Handle_MonitorSessionWithoutTwoFactors_Renews_WithOneLine()
    {
        _settings.ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Monitor);
        Token();
        SessionRow(PasswordOnly);

        var result = await Refresh();

        result.IsError.Should().BeFalse();
        _log.Entries.Should().ContainSingle(entry => entry.Values.Contains(AppMfaSignalCodes.WouldRequire));
        _revocation.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_MonitorReadFailure_RenewsAsBefore()
    {
        // Under Monitor the read stays on M5: a failure is Unknown and a warning, never a refusal.
        _settings.ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Monitor);
        Token();
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database"));

        (await Refresh()).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_FlagOff_NeverReadsTheRowForTheRequirement()
    {
        Token(requireTwoFactor: false);
        SessionRow(PasswordOnly);

        (await Refresh()).IsError.Should().BeFalse();
        _log.Entries.Should().NotContain(entry =>
            entry.Values.Contains(AppMfaSignalCodes.WouldRequire) || entry.Values.Contains(AppMfaSignalCodes.Refused));
    }

    [Fact]
    public async Task Handle_PlatformToken_StaysOnM5_UnderEnforce()
    {
        // ARR-0791 (2): the requirement is an application's; a platform token has none.
        Token(platform: true);
        _sessions.Setup(r => r.GetByIdAsync(_sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database"));

        (await Refresh()).IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ApplicationToken_CarriesNoAmrOrAuthTime_AfterASatisfiedRead()
    {
        // ARR-0777 (a), refresh path: the methods decide, the token never carries them.
        Token();
        SessionRow(PasswordAndTotp);

        (await Refresh()).IsError.Should().BeFalse();

        _jwt.Verify(s => s.GenerateAccessToken(
            It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
            It.Is<AccessTokenAuthentication>(a => a == AccessTokenAuthentication.Unrecorded),
            It.IsAny<Guid?>(), It.IsAny<IEnumerable<(Guid, string)>?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()), Times.Once);
    }
}
