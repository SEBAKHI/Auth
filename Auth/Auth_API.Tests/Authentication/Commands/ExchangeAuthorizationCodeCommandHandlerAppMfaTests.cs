using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ApplicationEntity = Auth.Domain.Entities.Application;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// S23, AM5: the code exchange of an application that requires two-step verification.
/// What the code proved is what its IdP session proved (D-S23-2 (a)); under Enforce a code
/// whose session did not prove two factors, or that carries no session, is refused with
/// <see cref="ApplicationErrors.TwoFactorRequired"/>; under Monitor it is let through with
/// one line. Either way the builder records the methods on the session row, and never
/// learns that a password was proved (ARR-0776, G8).
/// </summary>
/// <remarks>
/// Deliberate break: pass <see cref="AuthenticationMethods.Unknown"/> to the builder again;
/// the "passes the session's methods" test turns red.
/// </remarks>
public class ExchangeAuthorizationCodeCommandHandlerAppMfaTests
{
    private const string ClientId = "CRM";
    private const string RedirectUri = "https://crm.example.com/callback";
    private static readonly string Verifier = new('v', 43);
    private static readonly AuthenticationMethods PasswordOnly = AuthenticationMethods.Password;
    private static readonly AuthenticationMethods PasswordAndTotp = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private readonly Mock<IAuthorizationCodeRepository> _codes = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IIdpSessionRepository> _idpSessions = new();
    private readonly FakeCredentialHasher _hashes = new();
    private readonly Mock<ILoginResponseBuilder> _builder = new();
    private readonly CapturingLogger<ExchangeAuthorizationCodeCommandHandler> _log = new();
    private readonly IdentityProviderSettings _settings = new()
    {
        ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Enforce)
    };

    private readonly ExchangeAuthorizationCodeCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public ExchangeAuthorizationCodeCommandHandlerAppMfaTests()
    {
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _users.Setup(r => r.GetByIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _userId));
        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
                It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .ReturnsAsync(new LoginResponse
            {
                Token = new TokenResponse { AccessToken = "access-jwt", RefreshToken = "refresh", ExpiresIn = 900, RefreshExpiresIn = 604800 }
            });

        _handler = new ExchangeAuthorizationCodeCommandHandler(
            _codes.Object, _applications.Object, _access.Object, _users.Object, _idpSessions.Object, _hashes,
            _builder.Object, new Mock<ICredentialRevocationService>().Object, Mock.Of<IClientAuthenticator>(),
            TestHelpers.CreateOptions(_settings), _log);
    }

    private static string S256(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A tagged (or untagged) application, and a code issued from an IdP session that proved <paramref name="methods"/>.</summary>
    private void Arrange(AuthenticationMethods? methods, bool requireTwoFactor = true)
    {
        var application = new ApplicationEntity(
            Guid.NewGuid(), ClientId, "CRM", null, "https://crm.example.com", null, null,
            isActive: true, allowSelfRegistration: false, requireTwoFactor: requireTwoFactor,
            requireEmailVerification: false, sessionTimeoutMinutes: 60, maxConcurrentSessions: 5,
            DateTime.UtcNow, Guid.NewGuid(), null, null, ApplicationAccessMode.Everyone);
        _applications.Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(application);

        // methods null: a code issued before codes recorded their IdP session (OI-154).
        Guid? idpSessionId = methods is null ? null : Guid.NewGuid();
        if (idpSessionId is Guid id)
        {
            _idpSessions.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IdpSession(
                    id, _userId, "idp-hash", DateTime.UtcNow, DateTime.UtcNow.AddHours(10), null, null, null,
                    authMethods: methods!.Value.ToStored()));
        }

        _hashes.Map("plain-code", "code-hash");
        var code = AuthorizationCode.Create(
            application.Id, _userId, "code-hash", RedirectUri, S256(Verifier), TimeSpan.FromSeconds(60),
            "127.0.0.1", ScopeSet.OpenIdOnly, idpSessionId: idpSessionId);
        _codes.Setup(r => r.ConsumeByCodeHashAsync(FakeCredentialHasher.CandidatesOf("code-hash"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(code);
    }

    private Task<ErrorOr.ErrorOr<OAuthTokenResponse>> Exchange() =>
        _handler.Handle(
            new ExchangeAuthorizationCodeCommand("plain-code", RedirectUri, ClientId, Verifier, "127.0.0.1", "agent"),
            CancellationToken.None);

    private void VerifyNothingBuilt() =>
        _builder.Verify(b => b.BuildAsync(
            It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(),
            It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Never);

    [Fact]
    public async Task Handle_EnforceSessionWithoutTwoFactors_ReturnsTwoFactorRequired()
    {
        Arrange(PasswordOnly);

        var result = await Exchange();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Forbidden);
        _log.Entries.Should().ContainSingle(entry => entry.Values.Contains(AppMfaSignalCodes.Refused));
        VerifyNothingBuilt();
    }

    [Fact]
    public async Task Handle_EnforceCodeWithoutASession_IsUnknown_AndReturnsTwoFactorRequired()
    {
        Arrange(methods: null);

        var result = await Exchange();

        result.FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
        VerifyNothingBuilt();
    }

    [Fact]
    public async Task Handle_EnforceSessionWithNothingRecorded_ReturnsTwoFactorRequired()
    {
        Arrange(AuthenticationMethods.Unknown);

        (await Exchange()).FirstError.Code.Should().Be(ApplicationErrors.TwoFactorRequired.Code);
    }

    [Fact]
    public async Task Handle_SatisfiedSession_PassesTheSessionsMethods_AndNeverProvesThePassword()
    {
        Arrange(PasswordAndTotp);

        var result = await Exchange();

        result.IsError.Should().BeFalse();
        // The methods hold Password, and still: passwordProven false (ARR-0776, G8).
        _builder.Verify(b => b.BuildAsync(
            It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            PasswordAndTotp, false, It.IsAny<CancellationToken>(),
            false, ClientId, It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Monitor_LetsTheCodeThrough_WithOneLine_AndTheMethodsRecorded()
    {
        _settings.ApplicationMfaEnforcement = nameof(ApplicationMfaEnforcementMode.Monitor);
        Arrange(PasswordOnly);

        var result = await Exchange();

        result.IsError.Should().BeFalse();
        _log.Entries.Should().ContainSingle(entry => entry.Values.Contains(AppMfaSignalCodes.WouldRequire));
        _builder.Verify(b => b.BuildAsync(
            It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            PasswordOnly, false, It.IsAny<CancellationToken>(),
            It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
            It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()), Times.Once);
    }

    [Fact]
    public async Task Handle_FlagOff_LetsAPasswordOnlyCodeThrough_WithNoLine()
    {
        Arrange(PasswordOnly, requireTwoFactor: false);

        var result = await Exchange();

        result.IsError.Should().BeFalse();
        _log.Entries.Should().NotContain(entry =>
            entry.Values.Contains(AppMfaSignalCodes.WouldRequire) || entry.Values.Contains(AppMfaSignalCodes.Refused));
    }
}
