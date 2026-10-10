using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.RefreshToken;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging;
using RefreshTokenEntity = Auth.Domain.Entities.RefreshToken;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-103 and OI-97 end to end at the handler. The session row is held here and
/// changed the way its SQL changes it: the refresh's guarded touch
/// (<c>UserSessionRepository.TouchOnRefreshAsync</c>), the whole-row write the
/// refresh used before (<c>UpdateAsync</c>), and the daily expiry sweep
/// (<c>MarkExpiredSessionsEndedAsync</c>), which ends a live row past its expiry.
/// The SQL itself is held by <c>UserSessionRepositorySqlGuardTests</c>.
/// </summary>
public class RefreshSessionSlidingTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly AuthenticationMethods PasswordAndTotp =
        AuthenticationMethods.Password.With(AuthenticationMethods.Totp);

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new();
    private readonly Mock<ITokenClaimsResolver> _claims = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IRefreshTokenKeyService> _keys = new();
    private readonly Mock<IUserSessionRepository> _sessions = new();

    // The session row as the database holds it. Signed in seven days and two
    // hours ago, so the expiry the sign-in gave it passed two hours ago, and the
    // daily sweep has not run since.
    private readonly DateTime _startedAt = DateTime.UtcNow.AddDays(-7).AddHours(-2);
    private DateTime _expiresAt;
    private DateTime _lastActivityAt;
    private DateTime? _endedAt;

    private int _issued;
    private (IEnumerable<string> Permissions, AccessTokenAuthentication Authentication)? _minted;
    private Action? _duringTheMint;

    public RefreshSessionSlidingTests()
    {
        _expiresAt = _startedAt.AddDays(7);
        _lastActivityAt = DateTime.UtcNow.AddMinutes(-15);

        _users.Setup(r => r.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: UserId));
        _claims.Setup(r => r.ResolveAsync(UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenClaims(["super-admin"], ["*"], []));
        _keys.Setup(s => s.ComputeTokenHash(It.IsAny<string>())).Returns<string>(value => "hash:" + value);
        _refreshTokens.Setup(r => r.HasLiveTokenInSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The chain is in use: the token presented first was issued a day ago,
        // and every rotation makes its replacement findable by its hash.
        var first = TestHelpers.CreateRefreshToken(
            userId: UserId, tokenHash: "hash:first", sessionId: SessionId,
            createdAt: DateTime.UtcNow.AddDays(-1), expiresAt: DateTime.UtcNow.AddDays(6));
        _refreshTokens.Setup(r => r.GetByTokenHashAsync("hash:first", It.IsAny<CancellationToken>()))
            .ReturnsAsync(first);
        _refreshTokens.Setup(r => r.TryRotateAsync(
                It.IsAny<RefreshTokenEntity>(), It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
            .Callback((RefreshTokenEntity _, RefreshTokenEntity replacement, CancellationToken _) =>
                _refreshTokens.Setup(r => r.GetByTokenHashAsync(replacement.TokenHash, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(replacement))
            .ReturnsAsync(true);

        _jwt.Setup(s => s.GenerateAccessToken(
                It.IsAny<User>(), It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<AccessTokenAuthentication>(), It.IsAny<Guid?>(),
                It.IsAny<IEnumerable<(Guid OrganizationId, string Code)>?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<TokenOrganization?>()))
            .Callback((User _, IEnumerable<string> permissions, IEnumerable<string> _, AccessTokenAuthentication authentication,
                Guid? _, IEnumerable<(Guid, string)>? _, string? _, string? _, TokenOrganization? _) =>
            {
                _minted = (permissions.ToList(), authentication);
                _duringTheMint?.Invoke();
            })
            .Returns("access-token");
        _jwt.Setup(s => s.GenerateRefreshToken()).Returns(() => "issued-" + ++_issued);
        _jwt.Setup(s => s.GetTokenId(It.IsAny<string>())).Returns("jti");

        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Row());

        // UPDATE … SET [LastActivityAt] = @Now,
        //   [ExpiresAt] = CASE WHEN @ExpiresAt > [ExpiresAt] THEN @ExpiresAt ELSE [ExpiresAt] END
        // WHERE [Id] = @Id AND [UserId] = @UserId AND [EndedAt] IS NULL
        _sessions.Setup(r => r.TouchOnRefreshAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback((Guid id, Guid userId, DateTime now, DateTime expiresAt, CancellationToken _) =>
            {
                if (id != SessionId || userId != UserId || _endedAt is not null)
                {
                    return;
                }

                _lastActivityAt = now;
                if (expiresAt > _expiresAt)
                {
                    _expiresAt = expiresAt;
                }
            })
            .Returns(Task.CompletedTask);

        // UPDATE … SET [LastActivityAt], [ExpiresAt], [EndedAt] = @TerminatedAt,
        //   [EndReason] = @TerminationReason WHERE [Id] = @Id
        _sessions.Setup(r => r.UpdateAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
            .Callback((UserSession session, CancellationToken _) =>
            {
                _lastActivityAt = session.LastActivityAt;
                _expiresAt = session.ExpiresAt;
                _endedAt = session.TerminatedAt;
            })
            .Returns(Task.CompletedTask);
    }

    private UserSession Row() => TestHelpers.CreateUserSession(
        id: SessionId, userId: UserId, createdAt: _startedAt, expiresAt: _expiresAt,
        lastActivityAt: _lastActivityAt, isActive: _endedAt is null, terminatedAt: _endedAt,
        terminationReason: _endedAt is null ? null : "timeout", methods: PasswordAndTotp);

    /// <summary>What the daily sweep does when it runs at <paramref name="at"/>.</summary>
    private void SweepAt(DateTime at)
    {
        if (_endedAt is null && _expiresAt < at)
        {
            _endedAt = _expiresAt;
        }
    }

    private Task<ErrorOr.ErrorOr<Auth.Application.DTOs.TokenResponse>> Refresh(string token) =>
        new RefreshTokenCommandHandler(
            _users.Object,
            _refreshTokens.Object,
            _claims.Object,
            TestHelpers.CreatePlatformMfaPolicy(enforce: true, hasEnabledFactor: true),
            Mock.Of<IApplicationRepository>(),
            Mock.Of<IApplicationAccessRepository>(),
            _jwt.Object,
            _keys.Object,
            _sessions.Object,
            Mock.Of<ICredentialRevocationService>(),
            Mock.Of<IPublisher>(),
            TestHelpers.CreateOptions(new JwtSettings
            {
                AccessTokenLifetimeMinutes = 15,
                RefreshTokenLifetimeDays = 7,
                RotateRefreshTokens = true
            }),
            Mock.Of<ILogger<RefreshTokenCommandHandler>>())
        .Handle(new RefreshTokenCommand(token, "127.0.0.1", "agent"), CancellationToken.None);

    [Fact]
    public async Task Handle_SessionOlderThanTheRefreshLifetime_WithALiveChain_KeepsALiveRowAndWhatItProved()
    {
        // T9: the platform administrator's session is past the expiry its sign-in
        // gave the row, and still in use.
        (await Refresh("first")).IsError.Should().BeFalse();

        _expiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1),
            "the row now lives as long as the refresh token just handed out");

        SweepAt(DateTime.UtcNow);
        _endedAt.Should().BeNull("a session whose refresh chain is in use is not swept");

        var second = await Refresh("issued-1");

        second.IsError.Should().BeFalse();
        _minted!.Value.Authentication.Methods.Should().Be(PasswordAndTotp,
            "the row is live, so the refresh still reads what the session proved");
        _minted.Value.Authentication.AuthTime.Should().Be(new DateTimeOffset(_startedAt, TimeSpan.Zero));
        _minted.Value.Authentication.Requirement.Should().Be(MfaRequirement.None);
        _minted.Value.Permissions.Should().Equal("*");
    }

    [Fact]
    public async Task Handle_SignOutLandingBetweenTheReadAndTheTouch_StaysASignOut()
    {
        // OI-97: the row is read before the mint; a sign-out ends it during the mint.
        _duringTheMint = () => _endedAt ??= DateTime.UtcNow;

        (await Refresh("first")).IsError.Should().BeFalse();

        _endedAt.Should().NotBeNull("the refresh must never write over a sign-out");
        _expiresAt.Should().Be(_startedAt.AddDays(7), "an ended row is not slid either");
    }
}
