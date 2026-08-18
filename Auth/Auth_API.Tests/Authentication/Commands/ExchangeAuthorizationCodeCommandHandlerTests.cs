using Auth.Domain.Constants;
using System.Security.Cryptography;
using System.Text;
using Auth.Application.DTOs;
using Auth.Application.Features.Authentication.TokenExchange;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth_API.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// Unit tests for ExchangeAuthorizationCodeCommandHandler.
/// </summary>
public class ExchangeAuthorizationCodeCommandHandlerTests
{
    private const string ClientId = "CRM";
    private const string RedirectUri = "https://app.example.com/callback";
    private static readonly string Verifier = new('v', 43);
    private static readonly string Challenge = ComputeS256(Verifier);

    private readonly Mock<IAuthorizationCodeRepository> _authorizationCodeRepositoryMock = new();
    private readonly Mock<IApplicationRepository> _applicationRepositoryMock = new();
    private readonly Mock<IApplicationAccessRepository> _applicationAccessRepositoryMock = new();
    private readonly Mock<IUserRepository> _userRepositoryMock = new();
    private readonly Mock<IRefreshTokenKeyService> _refreshTokenKeyServiceMock = new();
    private readonly Mock<ILoginResponseBuilder> _loginResponseBuilderMock = new();
    private readonly Mock<ICredentialRevocationService> _credentialRevocationMock = new();
    private readonly ExchangeAuthorizationCodeCommandHandler _handler;

    public ExchangeAuthorizationCodeCommandHandlerTests()
    {
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _handler = new ExchangeAuthorizationCodeCommandHandler(
            _authorizationCodeRepositoryMock.Object,
            _applicationRepositoryMock.Object,
            _applicationAccessRepositoryMock.Object,
            _userRepositoryMock.Object,
            _refreshTokenKeyServiceMock.Object,
            _loginResponseBuilderMock.Object,
            _credentialRevocationMock.Object,
            new Mock<ILogger<ExchangeAuthorizationCodeCommandHandler>>().Object);
    }

    private static string ComputeS256(string verifier)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static ExchangeAuthorizationCodeCommand CreateCommand(
        string? code = "plain-code",
        string? redirectUri = RedirectUri,
        string? clientId = ClientId,
        string? codeVerifier = null)
    {
        return new ExchangeAuthorizationCodeCommand(
            code,
            redirectUri,
            clientId,
            codeVerifier ?? Verifier,
            "127.0.0.1",
            "TestAgent/1.0");
    }

    private (Auth.Domain.Entities.Application Application, AuthorizationCode Code, Guid UserId) SetupHappyPath(
        string codeChallenge)
    {
        var userId = Guid.NewGuid();
        var application = TestHelpers.CreateApplication(code: ClientId);

        _refreshTokenKeyServiceMock
            .Setup(s => s.ComputeTokenHash("plain-code"))
            .Returns("code-hash");

        var authorizationCode = AuthorizationCode.Create(
            application.Id, userId, "code-hash", RedirectUri, codeChallenge,
            TimeSpan.FromSeconds(60), "127.0.0.1");

        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(authorizationCode);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(application);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId));

        return (application, authorizationCode, userId);
    }

    [Fact]
    public async Task Handle_MissingCode_ReturnsAuthorizationCodeInvalid()
    {
        // Act
        var result = await _handler.Handle(CreateCommand(code: null), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
    }

    [Fact]
    public async Task Handle_MalformedVerifier_ReturnsPkceVerificationFailed()
    {
        // Act — verifier below the RFC 7636 minimum length.
        var result = await _handler.Handle(CreateCommand(codeVerifier: "short"), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.PkceVerificationFailed);
    }

    [Fact]
    public async Task Handle_UnknownCode_ReturnsAuthorizationCodeInvalid()
    {
        // Arrange
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);
        _authorizationCodeRepositoryMock
            .Setup(r => r.GetByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
    }

    [Fact]
    public async Task Handle_ReplayedCode_ReturnsAuthorizationCodeInvalid()
    {
        // Arrange — consume finds nothing, but the code exists as consumed.
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        var consumed = new AuthorizationCode(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "code-hash", RedirectUri,
            Challenge, DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow.AddSeconds(30),
            DateTime.UtcNow.AddSeconds(-5), "127.0.0.1");
        _authorizationCodeRepositoryMock
            .Setup(r => r.GetByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumed);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExpiredCode_ReturnsAuthorizationCodeInvalid()
    {
        // Arrange — the atomic consume can still return a row whose ExpiresAt
        // has passed; expiry is checked after consumption.
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");

        var expired = new AuthorizationCode(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "code-hash", RedirectUri,
            Challenge, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow, "127.0.0.1");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
    }

    [Fact]
    public async Task Handle_ClientMismatch_ReturnsInvalidClient()
    {
        // Arrange — the code belongs to a different application.
        SetupHappyPath(Challenge);
        var otherApplication = TestHelpers.CreateApplication(code: ClientId);
        _applicationRepositoryMock
            .Setup(r => r.GetByCodeAsync(ClientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherApplication);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidClient);
    }

    [Fact]
    public async Task Handle_RedirectUriMismatch_ReturnsInvalidRedirectUri()
    {
        // Arrange
        SetupHappyPath(Challenge);

        // Act
        var result = await _handler.Handle(
            CreateCommand(redirectUri: "https://app.example.com/other"),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.InvalidRedirectUri);
    }

    [Fact]
    public async Task Handle_WrongVerifier_ReturnsPkceVerificationFailed()
    {
        // Arrange — well-formed verifier that does not hash to the challenge.
        SetupHappyPath(Challenge);

        // Act
        var result = await _handler.Handle(
            CreateCommand(codeVerifier: new string('w', 43)),
            CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.PkceVerificationFailed);
    }

    [Fact]
    public async Task Handle_LockedUser_ReturnsAccountLocked()
    {
        // Arrange
        var (_, code, userId) = SetupHappyPath(Challenge);
        _userRepositoryMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: userId, status: UserStatus.Locked));

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(UserErrors.AccountLocked);
    }

    [Fact]
    public async Task Handle_ValidExchange_ReturnsOAuthTokensWithoutIdpSession()
    {
        // Arrange
        var (_, _, userId) = SetupHappyPath(Challenge);

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                It.Is<User>(u => u.Id == userId),
                "127.0.0.1",
                "TestAgent/1.0",
                // No device id: an OAuth token call has no browser storage to
                // have kept one in.
                null,
                It.IsAny<CancellationToken>(),
                false,
                It.IsAny<string?>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse
            {
                Token = new TokenResponse
                {
                    AccessToken = "access-jwt",
                    RefreshToken = "refresh-token",
                    ExpiresIn = 900,
                    RefreshExpiresIn = 604800
                },
                User = new UserInfo
                {
                    Id = userId,
                    Email = "u@test.com",
                    FirstName = "Test",
                    LastName = "User"
                }
            });

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        result.Value.AccessToken.Should().Be("access-jwt");
        result.Value.TokenType.Should().Be("Bearer");
        result.Value.ExpiresIn.Should().Be(900);
        result.Value.RefreshToken.Should().Be("refresh-token");
        result.Value.RefreshExpiresIn.Should().Be(604800);

        // The token endpoint must never mint an IdP session (no browser there),
        // and the token must be scoped to THIS app (aud = client id) with the
        // app recorded on the refresh token so refreshes keep the same audience.
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                false,
                ClientId,
                It.IsAny<Guid?>()),
            Times.Once);
    }

    #region Entitlement gate

    [Fact]
    public async Task Handle_UnentitledUser_ReturnsAccessDenied()
    {
        // Arrange — entitlement was withdrawn between /authorize and /token.
        SetupHappyPath(Challenge);
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(ApplicationErrors.AccessDenied);
    }

    [Fact]
    public async Task Handle_UnentitledUser_StillConsumesTheCode()
    {
        // Arrange
        SetupHappyPath(Challenge);
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert — single-use means single-use. The code was spent before the
        // check, which is correct: the client must start a fresh round rather
        // than get a second chance at the same code.
        _authorizationCodeRepositoryMock.Verify(
            r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnentitledUser_MintsNoToken()
    {
        // Arrange
        SetupHappyPath(Challenge);
        _applicationAccessRepositoryMock
            .Setup(r => r.IsUserEntitledAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _handler.Handle(CreateCommand(), CancellationToken.None);

        // Assert
        _loginResponseBuilderMock.Verify(
            b => b.BuildAsync(
                It.IsAny<User>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>()),
            Times.Never);
    }

    #endregion
    [Fact]
    public async Task Handle_ReplayedCode_RevokesTheSessionThatCodeIssued()
    {
        // The gap this closes. Detecting the replay and leaving the tokens alive
        // is the worst of both: the theft is recorded and the thief keeps working
        // access until the refresh token expires (RFC 6749 4.1.2).
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        var consumed = new AuthorizationCode(
            Guid.NewGuid(), Guid.NewGuid(), userId, "code-hash", RedirectUri,
            Challenge, DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow.AddSeconds(30),
            DateTime.UtcNow.AddSeconds(-5), "127.0.0.1", sessionId);
        _authorizationCodeRepositoryMock
            .Setup(r => r.GetByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumed);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
        _credentialRevocationMock.Verify(
            s => s.TerminateSessionAsync(
                sessionId, null, TokenRevocationReasons.AuthorizationCodeReplay, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReplayedCode_WithNoIssuedSession_RevokesNothingAndStillRefuses()
    {
        // A code consumed before this column existed, or an exchange that died
        // between consuming and issuing. Nothing was minted, so there is nothing
        // to take back — and the caller must still be refused identically.
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        var consumed = new AuthorizationCode(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "code-hash", RedirectUri,
            Challenge, DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow.AddSeconds(30),
            DateTime.UtcNow.AddSeconds(-5), "127.0.0.1", issuedSessionId: null);
        _authorizationCodeRepositoryMock
            .Setup(r => r.GetByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumed);

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
        _credentialRevocationMock.Verify(
            s => s.TerminateSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReplayedCode_RevocationThrowing_StillAnswersInvalidCode()
    {
        // The response must not change shape because the revocation failed. A
        // caller probing with a stolen code learns nothing either way.
        var sessionId = Guid.NewGuid();
        _refreshTokenKeyServiceMock.Setup(s => s.ComputeTokenHash("plain-code")).Returns("code-hash");
        _authorizationCodeRepositoryMock
            .Setup(r => r.ConsumeByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthorizationCode?)null);

        var consumed = new AuthorizationCode(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "code-hash", RedirectUri,
            Challenge, DateTime.UtcNow.AddSeconds(-30), DateTime.UtcNow.AddSeconds(30),
            DateTime.UtcNow.AddSeconds(-5), "127.0.0.1", sessionId);
        _authorizationCodeRepositoryMock
            .Setup(r => r.GetByCodeHashAsync("code-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(consumed);
        _credentialRevocationMock
            .Setup(s => s.TerminateSessionAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unreachable"));

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Should().Be(AuthErrors.AuthorizationCodeInvalid);
    }
    [Fact]
    public async Task Handle_ValidExchange_BindsTheCodeToTheSessionItIssued()
    {
        // Without this stamp a replay has nothing to revoke — which is the state
        // every code was permanently in before. It is written after the tokens
        // exist, so it records something real rather than an intention.
        var (_, code, userId) = SetupHappyPath(Challenge);
        var sessionId = Guid.NewGuid();

        _loginResponseBuilderMock
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), false, It.IsAny<string?>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new LoginResponse
            {
                Token = new TokenResponse
                {
                    AccessToken = "access-jwt",
                    RefreshToken = "refresh-token",
                    ExpiresIn = 900,
                    RefreshExpiresIn = 604800
                },
                SessionId = sessionId
            });

        var result = await _handler.Handle(CreateCommand(), CancellationToken.None);

        result.IsError.Should().BeFalse();
        _authorizationCodeRepositoryMock.Verify(
            r => r.RecordIssuedSessionAsync(code.Id, sessionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SessionIdIsNeverPutOnTheWire()
    {
        // It already travels inside the access token as "sid"; the response body
        // has no need of it, and a field on the wire becomes a field someone
        // depends on.
        var property = typeof(LoginResponse).GetProperty(nameof(LoginResponse.SessionId));

        property!.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false)
            .Should().NotBeEmpty();
    }
}
