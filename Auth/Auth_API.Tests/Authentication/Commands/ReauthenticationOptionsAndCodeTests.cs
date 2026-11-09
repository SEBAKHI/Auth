using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.Reauthenticate;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// What the window asks for, and the code it asks for by email: read from the one rule the proof
/// itself is checked against, so the window never offers what the request would refuse.
/// </summary>
/// <remarks>
/// Deliberate break, verified red: the send without the requirement's check (a password account
/// receives a code that proves nothing).
/// </remarks>
public class ReauthenticationOptionsAndCodeTests
{
    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<IEmailCodeSender> _sender = new();
    private readonly EmailSettings _emailSettings = new() { Enabled = true };
    private User _user = TestHelpers.CreateUser(email: "owner@example.com");
    private TwoFactorChallenge? _opened;

    public ReauthenticationOptionsAndCodeTests()
    {
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TestHelpers.CreateUserSession(id: SessionId, userId: _user.Id));
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _user);
        _challenges.Setup(c => c.GetOrOpenLiveAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwoFactorChallenge candidate, CancellationToken _) => _opened = candidate);
        _sender.Setup(s => s.SendAsync(
                It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailCodeSent(DateTime.UtcNow.AddMinutes(5), "o***@example.com", 2));
    }

    private ReauthenticationGuard Guard() =>
        new(_sessions.Object, TestHelpers.CreateOptions(new TwoFactorSettings()), TimeProvider.System, NullLogger<ReauthenticationGuard>.Instance);

    private ReauthenticationPolicy Policy() =>
        new(_store.Object, TestHelpers.UsableFactors(_store.Object), TestHelpers.PasskeyOnlyAdministratorRuleOff(), TestHelpers.CreateOptions(_emailSettings));

    private Task<ErrorOr<ReauthenticationOptionsResponse>> OptionsAsync(Guid? sessionId) =>
        new GetReauthenticationOptionsQueryHandler(Guard(), _users.Object, Policy())
            .Handle(new GetReauthenticationOptionsQuery(_user.Id, sessionId), CancellationToken.None);

    private Task<ErrorOr<EmailCodeSent>> SendAsync(Guid? sessionId) =>
        new SendReauthenticationEmailCodeCommandHandler(
                Guard(),
                _users.Object,
                Policy(),
                new AccountEmailCodeChallenge(_challenges.Object, new FakeCredentialHasher(), Mock.Of<IJwtTokenService>(j => j.GenerateRefreshToken() == "token")),
                _sender.Object,
                TestHelpers.CreateSecondFactorLockout())
            .Handle(new SendReauthenticationEmailCodeCommand(_user.Id, sessionId, "203.0.113.7"), CancellationToken.None);

    private void GivenEmailedSecondFactor() =>
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                _user.Id, null, "[\"h\"]", isEnabled: false, failedAttempts: 0, lockedUntil: null,
                modifiedAt: DateTime.UtcNow, emailCodeEnabledAt: DateTime.UtcNow.AddDays(-1), emailFailedAttempts: 0));

    [Fact]
    public async Task TheOptions_SayWhatTheProofWillAsk()
    {
        var result = await OptionsAsync(SessionId);

        result.Value.Should().Be(new ReauthenticationOptionsResponse(
            Passkey: false, Password: true, EmailCode: false, SecondStep: nameof(ReauthenticationSecondStep.None), CanProveInPlace: true));
    }

    [Fact]
    public async Task TheOptions_OfADeadToken_AskToSignIn()
    {
        (await OptionsAsync(null)).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
    }

    [Fact]
    public async Task APasswordAccountWithNoEmailedStep_IsSentNoCode()
    {
        var result = await SendAsync(SessionId);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeUnavailable.Code);
        _sender.Verify(s => s.SendAsync(
            It.IsAny<User>(), It.IsAny<TwoFactorChallenge>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _challenges.Verify(c => c.GetOrOpenLiveAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADeadToken_IsSentNoCode()
    {
        (await SendAsync(null)).FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _challenges.Verify(c => c.GetOrOpenLiveAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAccountWithoutAPassword_IsSentItsCode_OnAChallengeThatAsksForNoFactorRow()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);

        var result = await SendAsync(SessionId);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        _opened!.Purpose.Should().Be(TwoFactorChallengePurpose.Reauthentication);
        _opened.PrimaryMethod.IsUnknown.Should().BeTrue("nothing came before this code");
        _opened.RequiresBoundEmailFactor.Should().BeFalse("its first factor is its mailbox: it has no factor row");
    }

    [Fact]
    public async Task AnEmailedSecondFactor_IsSentItsCode_AfterThePassword_OnTheFactorsOwnLimits()
    {
        GivenEmailedSecondFactor();

        var result = await SendAsync(SessionId);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        _opened!.PrimaryMethod.Should().Be(AuthenticationMethods.Password, "the only first factor the emailed code may follow");
        _opened.RequiresBoundEmailFactor.Should().BeTrue("a locked emailed code gets no new code");
    }

    [Fact]
    public async Task WithEmailOff_NoCodeIsSent()
    {
        _emailSettings.Enabled = false;
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);

        (await SendAsync(SessionId)).FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeUnavailable.Code);
    }
}
