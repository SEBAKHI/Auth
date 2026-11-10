using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.Reauthenticate;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// A signed-in person proving themself again with a password or an emailed code, with no new
/// sign-in (owner decision, 2026-11-09). The real password door, verifier, guard and policy run
/// under the handler: a stolen session and a guessing script meet the same counts as at a sign-in,
/// and nothing is stamped until the proof a sign-in would ask for is whole.
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the second step skipped; the password door replaced by a
/// bare hash check (a wrong password goes uncounted); the session stamped with the session's own
/// methods rather than this proof's; the code spent outside the commit. REVIEW-FIXES 152: the
/// event given no address (F25); the failure row not published (F25); the commit given the
/// request's token (F30); the replay line on the step-up's surface (R4); the publish moved above
/// the commit's outcome (F28: the "no audit event" checks, which could not fail before).
/// </remarks>
public class ReauthenticateCommandHandlerTests
{
    private const string Password = "correct horse";
    private const string StoredHash = "stored-password-hash";
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "TESTSECRET";
    private const string StoredCodes = "[ \"hash-1\", \"hash-2\" ]";
    private const long MatchedStep = 59_313_872;
    private const string TotpCode = "123456";
    private const string EmailedCode = "654321";
    private const string CodeHashes = "code-hash";
    private const string IdpCookie = "idp-cookie";
    private const string Ip = "203.0.113.7";

    private static readonly Guid SessionId = Guid.NewGuid();

    private readonly Mock<IUserSessionRepository> _sessions = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILoginAttemptRepository> _attempts = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<IOtpHasher> _otp = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly FakeCredentialHasher _keys = new();
    private readonly PasswordSettings _passwordSettings = TestHelpers.CreatePasswordSettings();
    private readonly EmailSettings _emailSettings = new() { Enabled = true };
    private readonly CapturingLogger<ReauthenticateCommandHandler> _logger = new();

    // The handler's clock: the proof's stamp is read by it (F30). The guard keeps the system's.
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private PasskeyOnlyAdministratorRule _passkeyOnlyAdministrator = TestHelpers.PasskeyOnlyAdministratorRuleOff();
    private User _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: StoredHash);

    public ReauthenticateCommandHandlerTests()
    {
        _keys.Map(IdpCookie, "idp-hash");
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TestHelpers.CreateUserSession(id: SessionId, userId: _user.Id, methods: AuthenticationMethods.EmailCode));
        _users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _user);
        _users.Setup(u => u.GetLockStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _user.LockState);
        _hasher.Setup(h => h.VerifyPassword(Password, StoredHash)).Returns(true);
        GivenCommit(LoginCommitOutcome.Committed);
    }

    private ReauthenticateCommandHandler Handler()
    {
        var lockout = TestHelpers.CreateSecondFactorLockout();
        var verifier = new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(_totp.Object, _protector.Object),
                new RecoveryCodeProofStrategy(_totp.Object),
                new EmailCodeProofStrategy(_otp.Object),
            ],
            lockout);

        return new ReauthenticateCommandHandler(
            new ReauthenticationGuard(
                _sessions.Object,
                TestHelpers.CreateOptions(new TwoFactorSettings()),
                TestHelpers.AccessToken(DateTime.UtcNow),
                TimeProvider.System,
                NullLogger<ReauthenticationGuard>.Instance),
            _users.Object,
            new ReauthenticationPolicy(
                _store.Object,
                TestHelpers.UsableFactors(_store.Object),
                _passkeyOnlyAdministrator,
                TestHelpers.CreateOptions(_emailSettings)),
            _passkeyOnlyAdministrator,
            TestHelpers.CreatePasswordDoor(_users.Object, _attempts.Object, _hasher.Object, _passwordSettings),
            _challenges.Object,
            _otp.Object,
            verifier,
            new EmailCodeFactorProof(
                _challenges.Object,
                verifier,
                lockout,
                Mock.Of<IEmailCodeSender>(),
                new AccountEmailCodeChallenge(_challenges.Object, _keys, Mock.Of<IJwtTokenService>())),
            _store.Object,
            _keys,
            _publisher.Object,
            new FixedTimeProvider(Now),
            _logger);
    }

    private ReauthenticateCommand WithPassword(string? secondStep = null, bool recovery = false, string password = Password) =>
        new(_user.Id, SessionId, IdpCookie, password, null, secondStep, recovery, Ip, "Agent/1.0", "device-1");

    private ReauthenticateCommand WithEmailCode(string code = EmailedCode, string? secondStep = null) =>
        new(_user.Id, SessionId, IdpCookie, null, code, secondStep, false, Ip, "Agent/1.0", "device-1");

    private void GivenCommit(LoginCommitOutcome outcome) =>
        _store.Setup(s => s.TryCommitSessionProofAsync(
                It.IsAny<Guid>(), It.IsAny<SessionProofCommit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    private void GivenAuthenticator()
    {
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(_user.Id, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 0, lockedUntil: null));
        _store.Setup(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _protector.Setup(p => p.UnprotectAsync(_user.Id, ProtectedSecret, It.IsAny<CancellationToken>())).ReturnsAsync(PlainSecret);
        _totp.Setup(t => t.ValidateCode(PlainSecret, TotpCode)).Returns(MatchedStep);
    }

    private void GivenEmailedSecondFactor()
    {
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                _user.Id, null, StoredCodes, isEnabled: false, failedAttempts: 0, lockedUntil: null,
                modifiedAt: DateTime.UtcNow, emailCodeEnabledAt: DateTime.UtcNow.AddDays(-1), emailFailedAttempts: 0));
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private TwoFactorChallenge GivenLiveChallenge(AuthenticationMethods primary = default, bool attemptAvailable = true)
    {
        var challenge = new TwoFactorChallenge(
            Guid.NewGuid(), _user.Id, "token-hash", Ip, DateTime.UtcNow.AddMinutes(5), null, 0, DateTime.UtcNow, primary,
            TwoFactorChallengePurpose.Reauthentication, CodeHashes, DateTime.UtcNow, 1);

        _challenges.Setup(c => c.GetLiveForUserAsync(_user.Id, TwoFactorChallengePurpose.Reauthentication, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        _challenges.Setup(c => c.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.Reauthentication, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attemptAvailable ? 1 : null);
        _otp.Setup(o => o.Verify(EmailCodeProofStrategy.HashScope(challenge.Id), EmailedCode, CodeHashes)).Returns(true);

        return challenge;
    }

    private void VerifyNothingStamped() =>
        _store.Verify(s => s.TryCommitSessionProofAsync(
            It.IsAny<Guid>(), It.IsAny<SessionProofCommit>(), It.IsAny<CancellationToken>()), Times.Never);

    private void VerifyStamped(AuthenticationMethods methods, Func<SessionProofCommit, bool>? also = null) =>
        _store.Verify(s => s.TryCommitSessionProofAsync(
            _user.Id,
            It.Is<SessionProofCommit>(commit =>
                commit.Session.SessionId == SessionId
                && commit.Session.Method == methods
                && (also == null || also(commit))),
            It.IsAny<CancellationToken>()), Times.Once);

    // ── The session, before anything ──

    [Fact]
    public async Task NoSessionInTheToken_AsksToSignIn_WithNothingHashedOrCounted()
    {
        var result = await Handler().Handle(WithPassword() with { CurrentSessionId = null }, CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task SomeoneElsesSession_AsksToSignIn_WithNothingHashed()
    {
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(id: SessionId, userId: Guid.NewGuid()));

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnEndedSession_AsksToSignIn()
    {
        _sessions.Setup(r => r.GetByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUserSession(
                id: SessionId, userId: _user.Id, isActive: false, terminatedAt: DateTime.UtcNow));

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.ReauthenticationRequired.Code);
        VerifyNothingStamped();
    }

    // ── The password, through the sign-in's own door ──

    [Fact]
    public async Task ARightPassword_StampsTheSession_WithThePasswordAlone_AndWritesTheAuditEvent()
    {
        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        VerifyStamped(
            AuthenticationMethods.Password,
            commit => commit.FirstProofCode == null && commit.SecondFactor == null && commit.Session.IdpHashes != null);
        _publisher.Verify(p => p.Publish(
            It.Is<UserReauthenticatedEvent>(e =>
                e.UserId == _user.Id && e.SessionId == SessionId && e.Methods == AuthenticationMethods.Password
                && e.IpAddress == Ip && e.UserAgent == "Agent/1.0"),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AWrongPassword_IsCountedAsAtASignIn_AndAnswersTheFieldsOwnSentence()
    {
        var result = await Handler().Handle(WithPassword(password: "a guess"), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidCurrentPassword.Code, "the person is signed in: not a sign-in's refusal");
        _users.Verify(u => u.RecordFailedLoginAsync(
            _user.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once,
            "a stolen session must not be a way to guess the password for free");
        _attempts.Verify(a => a.CreateAsync(
            It.Is<LoginAttempt>(row => row.FailureReason == LoginFailureReasons.InvalidPassword), It.IsAny<CancellationToken>()), Times.Once,
            "the row the per-address ceiling counts");
        VerifyNothingStamped();
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticationFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "the first secret was wrong: the sign-in's own row says so");
    }

    [Fact]
    public async Task TheAddressCeiling_RefusesBeforeTheHash()
    {
        _attempts.Setup(a => a.CountFailedAttemptsForUserFromIpAsync(_user.Id, Ip, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_passwordSettings.MaxFailedAttempts);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(UserErrors.AccountLockedUntil(null).Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnAdministratorsLock_RefusesARightPassword()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: StoredHash, status: UserStatus.Locked);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        VerifyNothingStamped();
    }

    [Fact]
    public async Task APasswordOnAnAccountWithoutOne_ProvesNothing_AndCountsNothing()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidCurrentPassword.Code);
        _attempts.Verify(a => a.CreateAsync(It.IsAny<LoginAttempt>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task APasswordOnAnAddressNobodyProved_ProvesNothing()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: StoredHash, emailConfirmed: false);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidCurrentPassword.Code, "it may be a stranger's (D-215-5)");
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task APasskeyOnlyAdministrator_IsRefused_WithNothingHashed()
    {
        _passkeyOnlyAdministrator = TestHelpers.PasskeyOnlyAdministratorRuleOn(
            new QualifyingSecondFactors(Totp: false, Passkey: true), holdsPlatformAuthority: true);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(AuthErrors.PasskeyRequired.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    // ── The second step a sign-in would ask for ──

    [Fact]
    public async Task ThePasswordAlone_OnAnAccountWithItsAuthenticator_StampsNothing_AndCountsNoAttempt()
    {
        GivenAuthenticator();

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task ThePasswordAndItsAuthenticatorCode_AreOneCommit_ThatProvesTwoFactors()
    {
        GivenAuthenticator();

        var result = await Handler().Handle(WithPassword(secondStep: TotpCode), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        var methods = AuthenticationMethods.Password.With(AuthenticationMethods.Totp);
        methods.IsMfaSatisfied.Should().BeTrue();
        VerifyStamped(methods, commit => commit.SecondFactor!.Step == MatchedStep);
    }

    [Fact]
    public async Task AWrongAuthenticatorCode_IsCountedFirst_AndStampsNothing()
    {
        GivenAuthenticator();

        var result = await Handler().Handle(WithPassword(secondStep: "000000"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _store.Verify(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task ALockedAuthenticator_RefusesWithNothingChecked()
    {
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwoFactorSnapshot(
                _user.Id, ProtectedSecret, StoredCodes, isEnabled: true, failedAttempts: 5, lockedUntil: DateTime.UtcNow.AddMinutes(10)));

        var result = await Handler().Handle(WithPassword(secondStep: TotpCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _totp.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task ARecoveryCodeIssuedWithoutTwoFactors_ProvesNoSecondFactor()
    {
        // The emailed second factor's row: its codes were issued after the password alone (FA3).
        GivenEmailedSecondFactor();
        _store.Setup(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _totp.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "hash-1")).Returns(true);

        var result = await Handler().Handle(WithPassword(secondStep: "RECOVERY-1", recovery: true), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        VerifyStamped(AuthenticationMethods.Password, commit => commit.SecondFactor != null);
    }

    [Fact]
    public async Task ThePasswordAndAnEmailedSecondFactor_SpendTheCodeInTheCommit()
    {
        GivenEmailedSecondFactor();
        var challenge = GivenLiveChallenge(AuthenticationMethods.Password);

        var result = await Handler().Handle(WithPassword(secondStep: EmailedCode), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        VerifyStamped(
            AuthenticationMethods.Password.With(AuthenticationMethods.EmailCode),
            commit => commit.SecondFactor!.ChallengeId == challenge.Id && commit.FirstProofCode == null);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once,
            "the emailed code's own counter, as at a sign-in");
    }

    [Fact]
    public async Task WithEmailOff_TheEmailedSecondFactorsStep_TakesARecoveryCode_WhateverTheFormSaid()
    {
        _emailSettings.Enabled = false;
        GivenEmailedSecondFactor();
        _store.Setup(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _totp.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "hash-1")).Returns(true);

        var result = await Handler().Handle(WithPassword(secondStep: "RECOVERY-1"), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        _challenges.Verify(c => c.GetLiveForUserAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorChallengePurpose>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The emailed code of an account without a password ──

    [Fact]
    public async Task TheEmailedCode_OfAnAccountWithoutAPassword_IsItsFirstFactor_SpentInTheCommit()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);
        var challenge = GivenLiveChallenge();

        var result = await Handler().Handle(WithEmailCode(), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        var methods = AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false);
        methods.HasPrimary.Should().BeTrue();
        VerifyStamped(methods, commit => commit.FirstProofCode!.ChallengeId == challenge.Id && commit.FirstProofCode.CodesSeen == CodeHashes);
    }

    [Fact]
    public async Task AWrongEmailedCode_CostsAnAttempt_AndStampsNothing()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);
        var challenge = GivenLiveChallenge();

        var result = await Handler().Handle(WithEmailCode(code: "111111"), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        _challenges.Verify(c => c.TryReserveAttemptAsync(
            challenge.Id, TwoFactorChallengePurpose.Reauthentication, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()), Times.Once);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnEmailedCodeOutOfAttempts_IsRefused_ThoughItIsRight()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);
        GivenLiveChallenge(attemptAvailable: false);

        var result = await Handler().Handle(WithEmailCode(), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.EmailCodeInvalid.Code);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnEmailedCode_OnAPasswordAccount_ProvesNothing_AndCostsNoAttempt()
    {
        var challenge = GivenLiveChallenge();

        var result = await Handler().Handle(WithEmailCode(), CancellationToken.None);

        result.FirstError.Code.Should().Be(
            TwoFactorErrors.EmailCodeInvalid.Code, "a thief of the mailbox would otherwise pass for the person");
        _challenges.Verify(c => c.TryReserveAttemptAsync(
            challenge.Id, It.IsAny<TwoFactorChallengePurpose>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingStamped();
    }

    [Fact]
    public async Task AnAccountWithoutAPassword_IsStillAskedForItsAuthenticator()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null);
        GivenAuthenticator();
        GivenLiveChallenge();

        (await Handler().Handle(WithEmailCode(), CancellationToken.None)).FirstError.Code
            .Should().Be(UserErrors.InvalidTwoFactorCode.Code);
        VerifyNothingStamped();

        var result = await Handler().Handle(WithEmailCode(secondStep: TotpCode), CancellationToken.None);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        VerifyStamped(AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false).With(AuthenticationMethods.Totp));
    }

    [Fact]
    public async Task AnAdministratorsLock_RefusesTheEmailedCode_WithNoAttemptCharged()
    {
        _user = TestHelpers.CreateUser(email: "owner@example.com", passwordHash: null, status: UserStatus.Locked);
        var challenge = GivenLiveChallenge();

        var result = await Handler().Handle(WithEmailCode(), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _challenges.Verify(c => c.TryReserveAttemptAsync(
            challenge.Id, It.IsAny<TwoFactorChallengePurpose>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingStamped();
    }

    // ── The commit's answers ──

    public static TheoryData<LoginCommitOutcome, string> RefusedCommits => new()
    {
        { LoginCommitOutcome.StepReused, TwoFactorErrors.CodeAlreadyUsed.Code },
        { LoginCommitOutcome.RecoveryCodesChanged, TwoFactorErrors.InvalidRecoveryCode.Code },
        { LoginCommitOutcome.ChallengeLost, TwoFactorErrors.EmailCodeInvalid.Code },
        { LoginCommitOutcome.SessionLost, AuthErrors.ReauthenticationRequired.Code },
        { LoginCommitOutcome.FactorLost, UserErrors.TwoFactorNotEnabled.Code },
    };

    [Theory]
    [MemberData(nameof(RefusedCommits))]
    public async Task ACommitThatWroteNothing_IsRefused_AndNoAuditEventIsWritten(LoginCommitOutcome outcome, string code)
    {
        GivenCommit(outcome);

        var result = await Handler().Handle(WithPassword(), CancellationToken.None);

        result.FirstError.Code.Should().Be(code);
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── REVIEW-FIXES 152 ──

    [Fact]
    public async Task TheCommit_IsStampedByTheApisClock_AndNoDisconnectCancelsIt()
    {
        // F30: the guard judges the stamp by the API's clock, beside the token's iat; and the
        // second step's attempt is counted before the commit, so a client that leaves after a
        // right code must not have it counted as a wrong one.
        GivenAuthenticator();
        using var request = new CancellationTokenSource();

        var result = await Handler().Handle(WithPassword(secondStep: TotpCode), request.Token);

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        _store.Verify(s => s.TryCommitSessionProofAsync(
            _user.Id,
            It.Is<SessionProofCommit>(commit => commit.ProvedAtUtc == Now.UtcDateTime),
            It.Is<CancellationToken>(token => !token.CanBeCanceled)), Times.Once);
    }

    [Fact]
    public async Task ARightPassword_AndAWrongSecondStep_LeaveAFailureRow_WithWhereItCameFrom()
    {
        // F25: whoever holds the password is trying the second step.
        GivenAuthenticator();

        var result = await Handler().Handle(WithPassword(secondStep: "000000"), CancellationToken.None);

        result.IsError.Should().BeTrue();
        _publisher.Verify(p => p.Publish(
            It.Is<UserReauthenticationFailedEvent>(e =>
                e.UserId == _user.Id && e.SessionId == SessionId && e.Reason == result.FirstError.Code
                && e.IpAddress == Ip && e.UserAgent == "Agent/1.0"),
            CancellationToken.None), Times.Once);
        _publisher.Verify(p => p.Publish(It.IsAny<UserReauthenticatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AReusedCode_IsLoggedUnderTheWindowsOwnSurface_AndLeavesAFailureRow()
    {
        // R4: an operator counting replayed codes tells the window from a step-up.
        GivenAuthenticator();
        GivenCommit(LoginCommitOutcome.StepReused);

        var result = await Handler().Handle(WithPassword(secondStep: TotpCode), CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.CodeAlreadyUsed.Code);
        _logger.Entries.Should().ContainSingle(entry => entry.Message.StartsWith("Reused two-factor code rejected"))
            .Which.Values.Should().Contain(TotpReplayLog.Reauthentication);
        _publisher.Verify(p => p.Publish(
            It.Is<UserReauthenticationFailedEvent>(e => e.Reason == TwoFactorErrors.CodeAlreadyUsed.Code), CancellationToken.None), Times.Once);
    }

    [Fact]
    public void TheCommand_NeverPrintsAProof()
    {
        var text = new ReauthenticateCommand(
            Guid.NewGuid(), SessionId, "the-cookie", "the-password", "the-email-code", "the-second-code", false, Ip, null, null).ToString();

        text.Should().NotContain("the-cookie").And.NotContain("the-password").And.NotContain("the-email-code").And.NotContain("the-second-code");
    }
}
