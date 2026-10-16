using System.Security.Cryptography;
using System.Text;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.ClientCredentials;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Authentication;
using Auth_API.Tests.Authentication.ClientCredentials;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Authentication.ConfidentialClients;

/// <summary>
/// OI-171 PR B end to end inside the process: a server-side application (a Backend-for-Frontend)
/// redeems codes and renews tokens with <c>private_key_jwt</c> and a sign-in key. Real ES256
/// assertions, the real verifier and the real <see cref="ClientAuthenticator"/>; the code and
/// refresh-token tables are in memory, each write applying the conditions its SQL applies.
/// </summary>
public sealed class ConfidentialClientFlowTests : IDisposable
{
    private const string ServerSideUri = "https://shop.example.com/bff/callback";
    private const string PublicUri = "https://shop.example.com/callback";
    private const string Verifier = "vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv";
    private const string Assertion = ClientAuthenticator.JwtBearerAssertionType;

    private readonly MachineCredentials _signInKey = new();
    private readonly MachineCredentials _machineKey = new();
    private readonly MachineCredentials _otherApplicationsKey = new();
    private readonly Auth.Domain.Entities.Application _application = TestHelpers.CreateApplication(code: MachineCredentials.ClientId);
    private readonly Guid _otherApplicationId = Guid.NewGuid();
    private readonly User _user = TestHelpers.CreateUser();

    private readonly InMemoryAuthorizationCodes _codes = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly HashSet<(Guid, string)> _spentJtis = [];
    private readonly Mock<IApplicationMachineKeyRepository> _keys = new();
    private readonly Mock<IClientAssertionJtiStore> _jtis = new();
    private readonly Mock<IApplicationRepository> _applications = new();
    private readonly Mock<IApplicationAccessRepository> _access = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenKeyService> _hashes = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly RecordingLoginResponseBuilder _builder;
    private readonly JwtSettings _jwtSettings = new()
    {
        Issuer = MachineCredentials.Issuer,
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 7,
        RotateRefreshTokens = true,
        ApplicationRefreshReplayGraceSeconds = 30,
    };
    private int _issued;

    public ConfidentialClientFlowTests()
    {
        _builder = new RecordingLoginResponseBuilder(_tokens);
        _application.LoadRedirectUris([PublicUri]);
        _application.LoadConfidentialRedirectUris([ServerSideUri]);

        _keys.Setup(r => r.GetActiveByKidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string kid, CancellationToken _) => Credential(kid));
        _keys.Setup(r => r.RecordUsageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _jtis.Setup(s => s.TryConsumeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid application, string jti, DateTime _, CancellationToken _) => _spentJtis.Add((application, jti)));

        _applications.Setup(r => r.GetByCodeAsync(MachineCredentials.ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _access.Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _hashes.Setup(k => k.ComputeTokenHash(It.IsAny<string>())).Returns((string value) => "hash:" + value);

        _claims.Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims([], [], []));
        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "rotated-" + ++_issued);
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");
    }

    public void Dispose()
    {
        _signInKey.Dispose();
        _machineKey.Dispose();
        _otherApplicationsKey.Dispose();
    }

    private MachineKeyCredential? Credential(string kid)
    {
        if (kid == _signInKey.Kid)
        {
            return new MachineKeyCredential(Key(_signInKey, _application.Id, MachineKeyPurpose.SignIn), MachineCredentials.ClientId);
        }

        if (kid == _machineKey.Kid)
        {
            return new MachineKeyCredential(Key(_machineKey, _application.Id, MachineKeyPurpose.Machine), MachineCredentials.ClientId);
        }

        return kid == _otherApplicationsKey.Kid
            ? new MachineKeyCredential(Key(_otherApplicationsKey, _otherApplicationId, MachineKeyPurpose.SignIn), "other")
            : null;
    }

    private static ApplicationMachineKey Key(MachineCredentials credentials, Guid applicationId, MachineKeyPurpose purpose) => new(
        Guid.NewGuid(), applicationId, credentials.Kid, null, MachineClients.SigningAlgorithm,
        credentials.PublicKey.PublicKeyJwk, DateTime.UtcNow.AddDays(-1), Guid.NewGuid(), null, null, null, null, purpose);

    private ClientAuthenticator Authenticator() => new(
        _keys.Object, _jtis.Object, new ClientAssertionVerifier(TimeProvider.System),
        TestHelpers.CreateOptions(_jwtSettings), NullLogger<ClientAuthenticator>.Instance);

    // --- the codes ---

    private string IssueCode(string redirectUri, bool serverSide, string plain = "the-code")
    {
        _codes.Seed(AuthorizationCode.Create(
            _application.Id, _user.Id, "hash:" + plain, redirectUri, Challenge(Verifier),
            TimeSpan.FromSeconds(60), "127.0.0.1", ScopeSet.FromStored("openid"),
            requiresClientAuthentication: serverSide));
        return plain;
    }

    private static string Challenge(string verifier) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private Task<ErrorOr<OAuthTokenResponse>> Exchange(
        string code, string redirectUri, string? assertion, string? clientId = MachineCredentials.ClientId,
        string verifier = Verifier) =>
        new ExchangeAuthorizationCodeCommandHandler(
                _codes, _applications.Object, _access.Object, _users.Object, _hashes.Object,
                _builder, _revocation.Object, Authenticator(),
                NullLogger<ExchangeAuthorizationCodeCommandHandler>.Instance)
            .Handle(
                new ExchangeAuthorizationCodeCommand(
                    code, redirectUri, clientId, verifier, "203.0.113.7", "server",
                    ClientAssertionType: assertion is null ? null : Assertion, ClientAssertion: assertion),
                CancellationToken.None);

    // --- the refresh tokens ---

    private RefreshTokenCommandHandler RefreshHandler(IRefreshTokenRepository? tokens = null) => new(
        _users.Object, tokens ?? _tokens, _claims.Object, TestHelpers.CreatePlatformMfaPolicy(),
        _applications.Object, _access.Object, _jwt.Object, _hashes.Object,
        new Mock<IUserSessionRepository>().Object, _revocation.Object, new Mock<IIdpSessionReader>().Object,
        Authenticator(), _publisher.Object, TestHelpers.CreateOptions(_jwtSettings),
        NullLogger<RefreshTokenCommandHandler>.Instance);

    /// <summary>The token endpoint's refresh grant, as the controller builds it.</summary>
    private Task<ErrorOr<TokenResponse>> Refresh(string token, string? assertion, string? clientId = null) =>
        RefreshHandler().Handle(
            new RefreshTokenCommand(token, "203.0.113.7", "server", ClientId: clientId,
                ClientAssertionType: assertion is null ? null : Assertion, ClientAssertion: assertion),
            CancellationToken.None);

    /// <summary>The first-party refresh endpoint's command: cookie channel, no client, no assertion.</summary>
    private Task<ErrorOr<TokenResponse>> FirstPartyRefresh(string token) =>
        RefreshHandler().Handle(
            new RefreshTokenCommand(token, "203.0.113.7", "browser", ReplayGraceEligible: true),
            CancellationToken.None);

    private Guid SeedToken(string plain, bool bound, Guid? sessionId = null, Guid? applicationId = null, bool firstParty = false,
        DateTime? revokedAt = null, string? reason = null, string? replacedBy = null)
    {
        var session = sessionId ?? Guid.NewGuid();
        _tokens.Seed(TestHelpers.CreateRefreshToken(
            userId: _user.Id, tokenHash: "hash:" + plain, sessionId: session,
            applicationId: firstParty ? null : applicationId ?? _application.Id,
            scope: firstParty ? null : "openid", expiresAt: DateTime.UtcNow.AddDays(7),
            revokedAt: revokedAt, reasonRevoked: reason, replacedByTokenHash: replacedBy,
            requiresClientAuthentication: bound));
        return session;
    }

    private void NothingWasRevokedOrAnnounced()
    {
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<RefreshTokenReuseDetectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void ShouldBeInvalidClient<T>(ErrorOr<T> result)
    {
        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(AuthErrors.InvalidClient.Code);
    }

    // ===== TB1, TB2: a code issued to a server-side redirect URI =====

    [Fact]
    public async Task TB1_AServerSideCode_WithoutAnAssertion_IsInvalidClient_AndStaysRedeemableOnceWithTheKey()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri, assertion: null));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse("a request that did not authenticate consumes no server-side code");
        _builder.Calls.Should().BeEmpty();

        var redeemed = await Exchange(code, ServerSideUri, _signInKey.Assertion());
        redeemed.IsError.Should().BeFalse(redeemed.IsError ? redeemed.FirstError.Code : null);

        var again = await Exchange(code, ServerSideUri, _signInKey.Assertion());
        again.FirstError.Code.Should().Be(AuthErrors.AuthorizationCodeInvalid.Code, "redeemable once");
    }

    [Fact]
    public async Task TB2_AServerSideCode_WithASignInAssertion_IssuesABoundRefreshToken_LikeAPublicRedemption()
    {
        var publicCode = IssueCode(PublicUri, serverSide: false, plain: "public-code");
        var serverSideCode = IssueCode(ServerSideUri, serverSide: true, plain: "server-code");

        (await Exchange(publicCode, PublicUri, assertion: null)).IsError.Should().BeFalse();
        var confidential = await Exchange(serverSideCode, ServerSideUri, _signInKey.Assertion(), clientId: null);

        confidential.IsError.Should().BeFalse(confidential.IsError ? confidential.FirstError.Code : null);
        _builder.Calls.Should().HaveCount(2);
        var (publicCall, confidentialCall) = (_builder.Calls[0], _builder.Calls[1]);
        confidentialCall.Should().Be(publicCall with { RequiresClientAuthentication = true, IssuedToken = confidentialCall.IssuedToken },
            "the same audience, application, scope and session link as a public redemption; only the binding differs");
        confidentialCall.Audience.Should().Be(_application.Code);
        publicCall.RequiresClientAuthentication.Should().BeFalse();

        _tokens.ByHash("hash:" + confidential.Value.RefreshToken).RequiresClientAuthentication.Should().BeTrue();
        _tokens.ByHash("hash:" + _builder.Calls[0].IssuedToken).RequiresClientAuthentication.Should().BeFalse();
    }

    [Fact]
    public async Task CA4_AClientIdNamingAnotherApplication_WithTheKey_IsInvalidClient_AndTheCodeIsNotConsumed()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri, _signInKey.Assertion(), clientId: "other"));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse();
    }

    [Fact]
    public async Task CA6_PkceStaysMandatory_OnAServerSideCode()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);

        var result = await Exchange(code, ServerSideUri, _signInKey.Assertion(), verifier: new string('w', 43));

        result.FirstError.Code.Should().Be(AuthErrors.PkceVerificationFailed.Code);
        _builder.Calls.Should().BeEmpty();
    }

    // ===== TB3: one purpose per key =====

    [Fact]
    public async Task TB3_AMachineKey_RedeemsNoCode_AndRenewsNoToken()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);
        SeedToken("bound", bound: true);

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri, _machineKey.Assertion()));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse();

        ShouldBeInvalidClient(await Refresh("bound", _machineKey.Assertion()));
        _tokens.ByHash("hash:bound").IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task TB3_ASignInKey_GetsNoMachineToken()
    {
        using var settings = new JwtTokenService(Options.Create(new JwtSettings
        {
            Issuer = MachineCredentials.Issuer, Audience = "authsystem-api", KeyId = "test-key"
        }), Mock.Of<IPasswordHasher>());
        var handler = new ClientCredentialsCommandHandler(
            Authenticator(), _keys.Object, settings, NullLogger<ClientCredentialsCommandHandler>.Instance);

        var signIn = await handler.Handle(
            new ClientCredentialsCommand(Assertion, _signInKey.Assertion(), null, null, "203.0.113.7"), CancellationToken.None);
        var machine = await handler.Handle(
            new ClientCredentialsCommand(Assertion, _machineKey.Assertion(), null, null, "203.0.113.7"), CancellationToken.None);

        ShouldBeInvalidClient(signIn);
        machine.IsError.Should().BeFalse("the machine key of the same application still gets its token");
    }

    // ===== TB4: a key authenticates its own application only =====

    [Fact]
    public async Task TB4_AKeyOfAnotherApplication_RedeemsNothing_AndTheCodeIsNotConsumed()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);
        var others = _otherApplicationsKey.Assertion(d =>
        {
            d.Issuer = "other";
            d.Claims["sub"] = "other";
        });

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri, others, clientId: null));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse();
        _spentJtis.Should().ContainSingle(spent => spent.Item1 == _otherApplicationId,
            "the other key authenticated: what refused it is the application check, not the signature");

        (await Exchange(code, ServerSideUri, _signInKey.Assertion())).IsError.Should().BeFalse("the rightful server still can");
    }

    [Fact]
    public async Task TB4_AKeyOfAnotherApplication_RenewsNoToken()
    {
        SeedToken("bound", bound: true);
        var others = _otherApplicationsKey.Assertion(d =>
        {
            d.Issuer = "other";
            d.Claims["sub"] = "other";
        });

        ShouldBeInvalidClient(await Refresh("bound", others));
        _tokens.ByHash("hash:bound").IsRevoked.Should().BeFalse();
        _spentJtis.Should().ContainSingle(spent => spent.Item1 == _otherApplicationId,
            "the other key authenticated: what refused it is the application check, not the signature");
        NothingWasRevokedOrAnnounced();
    }

    // ===== TB8: the one intended change on the public path =====

    [Fact]
    public async Task TB8_APublicCode_WithAJunkAssertion_IsInvalidClient_AndIsNotConsumed()
    {
        var code = IssueCode(PublicUri, serverSide: false);

        ShouldBeInvalidClient(await Exchange(code, PublicUri, "not-a-jwt"));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse();

        (await Exchange(code, PublicUri, assertion: null)).IsError.Should().BeFalse("the public client still redeems it");
    }

    [Fact]
    public async Task CA5_APublicCode_WithAValidAssertion_IsAccepted_AndItsRefreshTokenIsBound()
    {
        var code = IssueCode(PublicUri, serverSide: false);

        var result = await Exchange(code, PublicUri, _signInKey.Assertion());

        result.IsError.Should().BeFalse();
        _tokens.ByHash("hash:" + result.Value.RefreshToken).RequiresClientAuthentication.Should().BeTrue();
    }

    // ===== TB14 (CA2b): only an authenticated replay revokes =====

    [Fact]
    public async Task TB14_AnUnauthenticatedReplay_OfARedeemedServerSideCode_RevokesNothing()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);
        (await Exchange(code, ServerSideUri, _signInKey.Assertion())).IsError.Should().BeFalse();

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri, assertion: null));

        _revocation.Verify(r => r.TerminateSessionAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "seeing a redeemed code is no way to end the server's session");
    }

    [Fact]
    public async Task TB14_AnAuthenticatedReplay_RevokesWhatTheCodeIssued_AsToday()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);
        (await Exchange(code, ServerSideUri, _signInKey.Assertion())).IsError.Should().BeFalse();
        var issuedSession = _codes.Stored("hash:" + code).IssuedSessionId;
        issuedSession.Should().NotBeNull();

        var replay = await Exchange(code, ServerSideUri, _signInKey.Assertion());

        replay.FirstError.Code.Should().Be(AuthErrors.AuthorizationCodeInvalid.Code);
        _revocation.Verify(r => r.TerminateSessionAsync(issuedSession!.Value, null, TokenRevocationReasons.AuthorizationCodeReplay, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TB14_APublicCodeRedeemedWithTheKey_IsTheServers_AnUnauthenticatedReplayRevokesNothing()
    {
        // CA5 then CA2b: the server upgraded a public code; the session it got is bound, so a
        // replay without the key must not be able to end it either.
        var code = IssueCode(PublicUri, serverSide: false);
        (await Exchange(code, PublicUri, _signInKey.Assertion())).IsError.Should().BeFalse();

        ShouldBeInvalidClient(await Exchange(code, PublicUri, assertion: null));

        _revocation.Verify(r => r.TerminateSessionAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task APublicCodesReplay_StillRevokes_WithoutAnyAssertion()
    {
        var code = IssueCode(PublicUri, serverSide: false);
        (await Exchange(code, PublicUri, assertion: null)).IsError.Should().BeFalse();

        (await Exchange(code, PublicUri, assertion: null)).FirstError.Code.Should().Be(AuthErrors.AuthorizationCodeInvalid.Code);

        _revocation.Verify(r => r.TerminateSessionAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== TB10: one jti, one use, across the grants; aud is the issuer =====

    [Fact]
    public async Task TB10_AnAssertionAcceptedOnOneGrant_IsRefusedOnAnother()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);
        SeedToken("bound", bound: true);
        var assertion = _signInKey.Assertion();

        (await Exchange(code, ServerSideUri, assertion)).IsError.Should().BeFalse();

        ShouldBeInvalidClient(await Refresh("bound", assertion));
        _tokens.ByHash("hash:bound").IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task TB10_AnAssertionAddressedToTheTokenEndpoint_IsRefused()
    {
        var code = IssueCode(ServerSideUri, serverSide: true);

        ShouldBeInvalidClient(await Exchange(code, ServerSideUri,
            _signInKey.Assertion(d => d.Audience = MachineCredentials.TokenEndpoint)));
        _codes.Stored("hash:" + code).IsConsumed.Should().BeFalse();
        _spentJtis.Should().BeEmpty("a refused assertion spends no jti");
    }

    // ===== TB5: a bound token without the key changes nothing =====

    [Fact]
    public async Task TB5_ABoundToken_WithoutAnAssertion_AtTheTokenEndpoint_IsInvalidClient_AndNothingChanges()
    {
        SeedToken("bound", bound: true);
        SeedToken("elsewhere", bound: false);

        ShouldBeInvalidClient(await Refresh("bound", assertion: null, clientId: MachineCredentials.ClientId));

        _tokens.ByHash("hash:bound").IsRevoked.Should().BeFalse("not rotated");
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeFalse("the user's other sessions are untouched");
        NothingWasRevokedOrAnnounced();
    }

    [Fact]
    public async Task TB5_ABoundToken_AtTheFirstPartyRefreshEndpoint_IsInvalidClient_AndNothingChanges()
    {
        // POST auth/refresh builds its command with no client and no assertion (AuthController):
        // the check lives in the handler, so it meets this endpoint too.
        SeedToken("bound", bound: true);

        ShouldBeInvalidClient(await FirstPartyRefresh("bound"));

        _tokens.ByHash("hash:bound").IsRevoked.Should().BeFalse();
        NothingWasRevokedOrAnnounced();
    }

    [Fact]
    public async Task TB5_ABoundRotatedToken_WithoutAnAssertion_IsNoButtonThatSignsThePersonOut()
    {
        // A stolen, already-rotated bound token: today's reuse branch would cascade over every
        // session of the user. Without the key it changes nothing.
        var session = SeedToken("current", bound: true);
        SeedToken("rotated", bound: true, sessionId: session,
            revokedAt: DateTime.UtcNow.AddMinutes(-10), reason: TokenRevocationReasons.Rotated, replacedBy: "hash:current");
        SeedToken("elsewhere", bound: false);

        ShouldBeInvalidClient(await Refresh("rotated", assertion: null));

        _tokens.ByHash("hash:current").IsRevoked.Should().BeFalse();
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeFalse();
        NothingWasRevokedOrAnnounced();
    }

    // ===== TB6: with the key, reuse detection is today's =====

    [Fact]
    public async Task TB6_ABoundRotatedToken_WithAValidAssertion_IsReuse_AsToday()
    {
        var session = SeedToken("current", bound: true);
        SeedToken("rotated", bound: true, sessionId: session,
            revokedAt: DateTime.UtcNow.AddMinutes(-10), reason: TokenRevocationReasons.Rotated, replacedBy: "hash:current");
        SeedToken("elsewhere", bound: false);

        var result = await Refresh("rotated", _signInKey.Assertion());

        result.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
        _tokens.ByHash("hash:current").IsRevoked.Should().BeTrue();
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeTrue("the cascade covers every token of the user");
        _revocation.Verify(r => r.RevokeAllCredentialsAsync(_user.Id, null, TokenRevocationReasons.RefreshTokenReuse, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TB6_TheFamilyRule_StillHolds_WithAValidAssertion()
    {
        // The session holds no live token any more: answered as ended, no second cascade.
        var session = Guid.NewGuid();
        SeedToken("rotated", bound: true, sessionId: session,
            revokedAt: DateTime.UtcNow.AddMinutes(-10), reason: TokenRevocationReasons.Rotated, replacedBy: "hash:gone");
        SeedToken("elsewhere", bound: false);

        var result = await Refresh("rotated", _signInKey.Assertion());

        result.FirstError.Code.Should().Be(AuthErrors.RefreshTokenRevoked.Code);
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeFalse();
        NothingWasRevokedOrAnnounced();
    }

    // ===== TB7: the application's replay grace, earned by the key =====

    [Fact]
    public async Task TB7_TwoAuthenticatedRefreshesOfOneBoundToken_WithinTheWindow_AreBothAnswered()
    {
        SeedToken("bound", bound: true);
        SeedToken("elsewhere", bound: false);

        var first = await Refresh("bound", _signInKey.Assertion());
        var second = await Refresh("bound", _signInKey.Assertion());

        first.IsError.Should().BeFalse(first.IsError ? first.FirstError.Code : null);
        second.IsError.Should().BeFalse(second.IsError ? second.FirstError.Code : null);
        _tokens.ByHash("hash:elsewhere").IsRevoked.Should().BeFalse();
        NothingWasRevokedOrAnnounced();
    }

    // ===== B5: what a refresh issues =====

    [Fact]
    public async Task B5_TheRotationOfABoundToken_IsBound()
    {
        SeedToken("bound", bound: true);

        var result = await Refresh("bound", _signInKey.Assertion());

        result.IsError.Should().BeFalse();
        _tokens.ByHash("hash:" + result.Value.RefreshToken).RequiresClientAuthentication.Should().BeTrue();
    }

    [Fact]
    public async Task CA5_TheFirstAuthenticatedRefreshOfAnUnboundToken_BindsItsChain()
    {
        SeedToken("public", bound: false);

        var result = await Refresh("public", _signInKey.Assertion());

        result.IsError.Should().BeFalse();
        var next = result.Value.RefreshToken;
        _tokens.ByHash("hash:" + next).RequiresClientAuthentication.Should().BeTrue();
        ShouldBeInvalidClient(await Refresh(next, assertion: null, clientId: MachineCredentials.ClientId));
    }

    [Fact]
    public async Task B5_ARaceLoserOfABoundChain_GetsABoundSibling()
    {
        // Another request of the same server rotates the token between this request's read and
        // its rotation: this one loses, and is answered with a sibling built from its rotation.
        SeedToken("bound", bound: true);
        var presented = _tokens.ByHash("hash:bound");
        var racing = new RacingRefreshTokens(_tokens, () =>
        {
            _tokens.Seed(TestHelpers.CreateRefreshToken(
                userId: _user.Id, tokenHash: "hash:winner", sessionId: presented.SessionId, applicationId: _application.Id,
                scope: "openid", expiresAt: DateTime.UtcNow.AddDays(7), requiresClientAuthentication: true));
            _tokens.Seed(TestHelpers.CreateRefreshToken(
                id: presented.Id, userId: _user.Id, tokenHash: "hash:bound", sessionId: presented.SessionId,
                applicationId: _application.Id, scope: "openid", expiresAt: presented.ExpiresAt,
                revokedAt: DateTime.UtcNow, reasonRevoked: TokenRevocationReasons.Rotated, replacedByTokenHash: "hash:winner",
                requiresClientAuthentication: true));
        });

        var result = await RefreshHandler(racing).Handle(
            new RefreshTokenCommand("bound", "203.0.113.7", "server",
                ClientAssertionType: Assertion, ClientAssertion: _signInKey.Assertion()),
            CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : null);
        racing.Raced.Should().BeTrue("the rotation was lost, so the sibling path answered");
        _tokens.ByHash("hash:" + result.Value.RefreshToken).RequiresClientAuthentication.Should().BeTrue();
        NothingWasRevokedOrAnnounced();
    }

    [Fact]
    public async Task AnUnboundToken_WithoutAnAssertion_RefreshesAsToday_AndStaysUnbound()
    {
        SeedToken("public", bound: false);

        var result = await Refresh("public", assertion: null, clientId: MachineCredentials.ClientId);

        result.IsError.Should().BeFalse();
        _tokens.ByHash("hash:" + result.Value.RefreshToken).RequiresClientAuthentication.Should().BeFalse();
    }

    [Fact]
    public async Task AFirstPartyToken_WithAnAssertion_IsInvalidClient()
    {
        SeedToken("platform", bound: false, firstParty: true);

        ShouldBeInvalidClient(await Refresh("platform", _signInKey.Assertion()));
        _tokens.ByHash("hash:platform").IsRevoked.Should().BeFalse();
        _spentJtis.Should().BeEmpty("refused before any key is read");
    }

    [Fact]
    public async Task TheGrace_NeverHandsABoundReplacement_ToARequestWithoutTheKey()
    {
        // The token before an upgrade, presented again right after the server's authenticated
        // refresh bound the chain: no answer from the bound replacement without the key.
        SeedToken("public", bound: false);
        var upgraded = await Refresh("public", _signInKey.Assertion());
        upgraded.IsError.Should().BeFalse();

        var replay = await Refresh("public", assertion: null, clientId: MachineCredentials.ClientId);

        // The presented token is a public one, rotated: what follows is today's reuse rule for it,
        // exactly as once the window has passed. What it never gets is a token.
        replay.FirstError.Code.Should().Be(AuthErrors.TokenRevoked.Code);
    }

    /// <summary>
    /// The login response builder, recording what each exchange asked of it and storing the
    /// refresh token it would have stored, with the binding it was given.
    /// </summary>
    private sealed class RecordingLoginResponseBuilder(InMemoryRefreshTokenRepository tokens) : ILoginResponseBuilder
    {
        public sealed record Call(
            Guid UserId, string? Audience, Guid? ApplicationId, string? Scope, Guid? LinkedIdpSessionId,
            bool EstablishIdpSession, bool RequiresClientAuthentication)
        {
            public string IssuedToken { get; init; } = string.Empty;
        }

        private int _count;

        public List<Call> Calls { get; } = [];

        public Task<ErrorOr<LoginResponse>> BuildAsync(
            User user, string? ipAddress, string? userAgent, string? deviceId,
            AuthenticationMethods authenticationMethods, bool passwordProven, CancellationToken cancellationToken,
            bool establishIdpSession = true, string? audience = null, Guid? applicationId = null, string? scope = null,
            Guid? twoFactorChallengeId = null, bool persistent = false, Guid? linkedIdpSessionId = null,
            bool requiresClientAuthentication = false)
        {
            var plain = "issued-" + ++_count;
            var session = Guid.NewGuid();
            tokens.Seed(TestHelpers.CreateRefreshToken(
                userId: user.Id, tokenHash: "hash:" + plain, sessionId: session, applicationId: applicationId,
                scope: scope, expiresAt: DateTime.UtcNow.AddDays(7), idpSessionId: linkedIdpSessionId,
                requiresClientAuthentication: requiresClientAuthentication));
            Calls.Add(new Call(user.Id, audience, applicationId, scope, linkedIdpSessionId, establishIdpSession,
                requiresClientAuthentication) { IssuedToken = plain });

            return Task.FromResult<ErrorOr<LoginResponse>>(new LoginResponse
            {
                SessionId = session,
                Token = new TokenResponse
                {
                    AccessToken = "access-token",
                    RefreshToken = plain,
                    ExpiresIn = 900,
                    RefreshExpiresIn = 604800,
                    Scope = scope
                }
            });
        }
    }

    /// <summary>
    /// The in-memory table, with a competing rotation run just before this request's rotation:
    /// the conditional update then finds the token already revoked, as the SQL would.
    /// </summary>
    private sealed class RacingRefreshTokens(InMemoryRefreshTokenRepository inner, Action competitor) : IRefreshTokenRepository
    {
        public bool Raced { get; private set; }

        public async Task<bool> TryRotateAsync(RefreshToken revokedOld, RefreshToken replacement, CancellationToken cancellationToken)
        {
            if (!Raced)
            {
                Raced = true;
                competitor();
            }

            return await inner.TryRotateAsync(revokedOld, replacement, cancellationToken);
        }

        public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => inner.GetByIdAsync(id, cancellationToken);
        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) => inner.GetByTokenHashAsync(tokenHash, cancellationToken);
        public Task<RefreshToken> CreateAsync(RefreshToken token, CancellationToken cancellationToken) => inner.CreateAsync(token, cancellationToken);
        public Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken) => inner.UpdateAsync(token, cancellationToken);
        public Task<int> RevokeAllForUserAsync(Guid userId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForUserAsync(userId, revokedBy, reason, cancellationToken);
        public Task RevokeByDeviceAsync(Guid userId, string deviceInfo, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeByDeviceAsync(userId, deviceInfo, revokedBy, reason, cancellationToken);
        public Task RevokeBySessionIdAsync(Guid sessionId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeBySessionIdAsync(sessionId, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeAllForApplicationAsync(Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeAllForApplicationAsync(applicationId, revokedBy, reason, cancellationToken);
        public Task<IReadOnlyList<Guid>> RevokeForUserAndApplicationAsync(Guid userId, Guid applicationId, Guid? revokedBy, string reason, CancellationToken cancellationToken) => inner.RevokeForUserAndApplicationAsync(userId, applicationId, revokedBy, reason, cancellationToken);
        public Task<bool> HasLiveTokenInSessionAsync(Guid sessionId, CancellationToken cancellationToken) => inner.HasLiveTokenInSessionAsync(sessionId, cancellationToken);
        public Task<int> RelinkIdpSessionAsync(Guid userId, Guid fromIdpSessionId, Guid toIdpSessionId, CancellationToken cancellationToken) => inner.RelinkIdpSessionAsync(userId, fromIdpSessionId, toIdpSessionId, cancellationToken);
        public Task<IReadOnlyList<RefreshToken>> GetActiveTokensForUserAsync(Guid userId, CancellationToken cancellationToken) => inner.GetActiveTokensForUserAsync(userId, cancellationToken);
        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) => inner.CleanupExpiredAsync(olderThanUtc, batchSize, cancellationToken);
    }
}
