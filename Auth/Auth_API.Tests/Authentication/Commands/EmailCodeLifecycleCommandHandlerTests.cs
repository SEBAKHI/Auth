using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.ConfirmEmailCode;
using Auth.Application.Features.Authentication.DisableEmailCode;
using Auth.Application.Features.Authentication.EnableEmailCode;
using Auth.Application.Features.Authentication.EnableTwoFactor;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Primitives;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// The emailed second factor switched on, off, and over to and from the authenticator
/// app (S24 PR B, EO8, D-T31 B, D-T34 B, OI-220 (2), (3), (6)). The real verifier and its
/// three strategies run under the handlers; storage, sending, hashing and TOTP arithmetic
/// are stubbed, so each test follows a request from its first refusal to its commit and
/// what must follow it: the events, the one notice, and signing the other sessions out.
/// </summary>
public class EmailCodeLifecycleCommandHandlerTests
{
    private const string DeviceName = "Firefox on Linux";
    private const string ClientIp = "203.0.113.9";
    private const string IdpCookie = "idp-cookie";
    private const string AppSecret = "v2:app-secret";
    private const long Step = 59_313_872;

    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly string[] GeneratedCodes = ["AAAA-BBBB", "CCCC-DDDD"];

    private readonly User _user = TestHelpers.CreateUser(email: "owner@example.com");
    private readonly Mock<IReauthenticationGuard> _guard = new();
    private readonly Mock<IPlatformMfaPolicy> _policy = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IEmailCodeSender> _sender = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _protector = new();
    private readonly Mock<ICredentialRevocationService> _revocation = new();
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly EmailSettings _email = new() { Enabled = true };
    private readonly TwoFactorSettings _twoFactor = new() { RequireEmailCodeForFirstFactor = false };
    private readonly EmailCodeSent _sent = new(DateTime.UtcNow.AddMinutes(10), "o***r@example.com", 2);

    public EmailCodeLifecycleCommandHandlerTests()
    {
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("unhanded-token");
        _totp.Setup(t => t.GenerateRecoveryCodes(10)).Returns(GeneratedCodes);
        _totp.Setup(t => t.HashRecoveryCode(It.IsAny<string>())).Returns<string>(code => $"hash:{code}");
        _totp.Setup(t => t.VerifyRecoveryCode("RECOVERY-1", "h1")).Returns(true);
        _sender.Setup(s => s.SendAsync(_user, It.IsAny<TwoFactorChallenge>(), ClientIp, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_sent);
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _store.Setup(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        GivenSession(AuthenticationMethods.Password);
    }

    // ── Wiring ──

    private SecondFactorVerifier Verifier() => new(
        _store.Object,
        [
            new TotpProofStrategy(_totp.Object, _protector.Object),
            new RecoveryCodeProofStrategy(_totp.Object),
            new EmailCodeProofStrategy(ScopedOtpHasher.Instance)
        ],
        TestHelpers.CreateSecondFactorLockout());

    private EmailCodeFactorProof FactorProof() => new(
        _challenges.Object, Verifier(), TestHelpers.CreateSecondFactorLockout(), _sender.Object, _jwt.Object, new FakeCredentialHasher());

    private EnableEmailCodeCommandHandler EnableHandler() => new(
        _guard.Object, _policy.Object, _store.Object, _challenges.Object, _users.Object, _sender.Object,
        TestHelpers.CreateSecondFactorLockout(), _jwt.Object, new FakeCredentialHasher(),
        TestHelpers.CreateOptions(_email), NullLogger<EnableEmailCodeCommandHandler>.Instance);

    private ConfirmEmailCodeCommandHandler ConfirmHandler() => new(
        _guard.Object, _policy.Object, _store.Object, _challenges.Object, _users.Object, ScopedOtpHasher.Instance, _totp.Object,
        _revocation.Object, _dispatcher.Object, TestHelpers.CreateOptions(_email), NullLogger<ConfirmEmailCodeCommandHandler>.Instance);

    private DisableEmailCodeCommandHandler DisableHandler() => new(
        _guard.Object, _store.Object, _users.Object, FactorProof(), _revocation.Object, _dispatcher.Object,
        NullLogger<DisableEmailCodeCommandHandler>.Instance);

    private EnableTwoFactorCommandHandler EnableAppHandler() => new(
        _guard.Object,
        Verifier(),
        _store.Object,
        _totp.Object,
        new TotpReplayPolicy(TestHelpers.CreateOptions(_twoFactor)),
        new FirstFactorEmailProofPolicy(TestHelpers.CreateOptions(_twoFactor), TestHelpers.CreateOptions(_email)),
        new FirstFactorEmailProof(
            Mock.Of<ITwoFactorBindCodeRepository>(MockBehavior.Strict), _users.Object, Mock.Of<INotificationService>(MockBehavior.Strict),
            Mock.Of<IOtpGenerator>(MockBehavior.Strict), ScopedOtpHasher.Instance, TestHelpers.CreateOptions(_email), TimeProvider.System,
            NullLogger<FirstFactorEmailProof>.Instance),
        _users.Object,
        new FakeCredentialHasher(),
        _dispatcher.Object,
        FactorProof(),
        _revocation.Object,
        NullLogger<EnableTwoFactorCommandHandler>.Instance);

    private void GivenSession(AuthenticationMethods methods) =>
        _guard.Setup(g => g.EnsureRecentSignInAsync(_user.Id, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecentSession(SessionId, DeviceName, methods));

    private void GivenRow(TwoFactorSnapshot? row) =>
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);

    private TwoFactorChallenge GivenLive(TwoFactorChallengePurpose purpose, params string[] codes)
    {
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.Password, purpose, codes);
        _challenges.Setup(c => c.GetLiveForUserAsync(_user.Id, purpose, It.IsAny<CancellationToken>())).ReturnsAsync(challenge);
        _challenges.Setup(c => c.TryReserveAttemptAsync(challenge.Id, purpose, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return challenge;
    }

    private static TwoFactorSnapshot App(Guid userId) =>
        new(userId, AppSecret, "[\"h1\"]", isEnabled: true, failedAttempts: 0, lockedUntil: null, recoveryCodesQualified: true);

    private IReadOnlyList<IDomainEvent> Events => _user.DomainEvents;

    private void VerifyOtherSessionsSignedOut(Times times) =>
        _revocation.Verify(r => r.RevokeCredentialsAsync(
            _user.Id, SessionId, IdpCookie, _user.Id, It.IsAny<string>(), CancellationToken.None), times);

    private void VerifyNothingCounted()
    {
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _challenges.Verify(c => c.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<TwoFactorChallengePurpose>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── email/enable ──

    private Task<ErrorOr<EmailCodeSent>> Enable() =>
        EnableHandler().Handle(new EnableEmailCodeCommand(_user.Id, SessionId, ClientIp), CancellationToken.None);

    [Fact]
    public async Task Handle_Enable_WithoutARecentSignIn_ReturnsReauthenticationRequired_AndSendsNothing()
    {
        _guard.Setup(g => g.EnsureRecentSignInAsync(_user.Id, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthErrors.ReauthenticationRequired);

        var result = await Enable();

        result.FirstError.Should().Be(AuthErrors.ReauthenticationRequired);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_WhileEmailIsOff_ReturnsEmailCodeUnavailable_AndOpensNothing()
    {
        _email.Enabled = false;

        var result = await Enable();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        _challenges.Verify(c => c.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_OnAnAccountWithoutAPassword_ReturnsEmailCodeRequiresPassword()
    {
        var external = TestHelpers.CreateUser(id: _user.Id, passwordHash: null);
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(external);

        var result = await Enable();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeRequiresPassword);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_WithAnUnconfirmedAddress_ReturnsEmailCodeRecipientUnavailable()
    {
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _user.Id, emailConfirmed: false));

        var result = await Enable();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeRecipientUnavailable);
    }

    [Fact]
    public async Task Handle_Enable_WhenTheEmailedCodeIsBoundAlready_ReturnsAlreadyEnabled()
    {
        GivenRow(EmailRow(_user.Id));

        var result = await Enable();

        result.FirstError.Should().Be(UserErrors.TwoFactorAlreadyEnabled);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_OverABoundApp_FromASessionThatProvedThePasswordAlone_ReturnsReauthenticationRequired()
    {
        // D-T31 B: whoever holds the password alone may not swap the owner's app for a mailbox.
        GivenRow(App(_user.Id));

        var result = await Enable();

        result.FirstError.Should().Be(AuthErrors.ReauthenticationRequired);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_OverABoundApp_ForAnAdministratorUnderEnforcement_ReturnsRequiredByPolicy()
    {
        GivenRow(App(_user.Id));
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        _policy.Setup(p => p.IsEnforcedForUserAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Enable();

        result.FirstError.Should().Be(TwoFactorErrors.RequiredByPolicy);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_Enable_OpensAnEnrollmentChallenge_AndSendsItsCode()
    {
        GivenRow(null);
        TwoFactorChallenge? opened = null;
        _challenges.Setup(c => c.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<TwoFactorChallenge, CancellationToken>((challenge, _) => opened = challenge);

        var result = await Enable();

        result.Value.Should().Be(_sent);
        opened!.Purpose.Should().Be(TwoFactorChallengePurpose.EmailEnrollment);
        opened.UserId.Should().Be(_user.Id);
        _sender.Verify(s => s.SendAsync(_user, opened, ClientIp, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Enable_OverABoundApp_FromATwoFactorSession_ReusesTheLiveChallenge()
    {
        GivenRow(App(_user.Id));
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        var live = GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");

        var result = await Enable();

        result.IsError.Should().BeFalse();
        _challenges.Verify(c => c.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()), Times.Never);
        _sender.Verify(s => s.SendAsync(_user, live, ClientIp, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── email/confirm ──

    private Task<ErrorOr<EmailCodeConfirmedResponse>> Confirm(string code = "111111") =>
        ConfirmHandler().Handle(new ConfirmEmailCodeCommand(_user.Id, code, SessionId, IdpCookie, ClientIp), CancellationToken.None);

    private void GivenBind(params LoginCommitOutcome[] outcomes)
    {
        var sequence = _store.SetupSequence(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()));
        foreach (var outcome in outcomes)
        {
            sequence = sequence.ReturnsAsync(outcome);
        }
    }

    [Fact]
    public async Task Handle_Confirm_WithoutALiveEnrollment_ReturnsEmailCodeInvalid_AndCountsNothing()
    {
        GivenRow(null);

        var result = await Confirm();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeInvalid);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_Confirm_WhileEmailIsOff_ReturnsEmailCodeUnavailable()
    {
        GivenRow(null);
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");
        _email.Enabled = false;

        var result = await Confirm();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_Confirm_WithAWrongCode_ReturnsEmailCodeInvalid_AfterCountingAnAttemptOnTheChallenge()
    {
        GivenRow(null);
        var challenge = GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");

        var result = await Confirm("999999");

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeInvalid);
        _challenges.Verify(c => c.TryReserveAttemptAsync(
            challenge.Id, TwoFactorChallengePurpose.EmailEnrollment, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Confirm_WithACodeANewerOneReplaced_ReturnsEmailCodeInvalid_TheOneShapeBindingAlwaysGives()
    {
        GivenRow(null);
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111", "222222");

        var result = await Confirm("111111");

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeInvalid);
    }

    [Fact]
    public async Task Handle_Confirm_OnAnAccountWithNoFactor_BindsTheEmailedCode_IssuesUnqualifiedCodes_AndSignsNobodyOut()
    {
        GivenRow(null);
        var challenge = GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");
        EmailCodeBinding? binding = null;
        _store.Setup(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()))
            .Callback<EmailCodeBinding, CancellationToken>((b, _) => binding = b)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Confirm();

        result.Value.RecoveryCodes.Should().Equal(GeneratedCodes);
        binding!.ChallengeId.Should().Be(challenge.Id);
        binding.EmailCodesSeen.Should().Be(challenge.EmailCodeHash);
        binding.FactorSeen.Should().BeNull();
        binding.NewRecoveryCodesJson.Should().Contain("hash:AAAA-BBBB");
        _user.TwoFactorEnabled.Should().BeTrue();
        Events.Should().ContainSingle().Which.Should().BeOfType<TwoFactorEmailCodeEnabledEvent>()
            .Which.ReplacedAuthenticator.Should().BeFalse();
        VerifyOtherSessionsSignedOut(Times.Never());
        _dispatcher.Verify(d => d.DispatchEventsAsync(_user, CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_Confirm_OverABoundApp_KeepsItsCodes_RaisesBothEvents_AndSignsTheOtherSessionsOut()
    {
        // D-T34 B: the emailed code takes the app's place; the app's codes stay, with
        // their qualification; one notice (the app's removal is marked replaced).
        GivenRow(App(_user.Id));
        GivenSession(AuthenticationMethods.Password.With(AuthenticationMethods.Totp));
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");
        EmailCodeBinding? binding = null;
        _store.Setup(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()))
            .Callback<EmailCodeBinding, CancellationToken>((b, _) => binding = b)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Confirm();

        result.Value.RecoveryCodes.Should().BeNull("the codes the app issued stay");
        binding!.FactorSeen!.IsEnabled.Should().BeTrue();
        binding.NewRecoveryCodesJson.Should().BeNull();
        _totp.Verify(t => t.GenerateRecoveryCodes(It.IsAny<int>()), Times.Never);
        Events.Should().HaveCount(2);
        Events[0].Should().BeOfType<TwoFactorDisabledEvent>().Which.ReplacedByEmailCode.Should().BeTrue();
        Events[1].Should().BeOfType<TwoFactorEmailCodeEnabledEvent>().Which.ReplacedAuthenticator.Should().BeTrue();
        VerifyOtherSessionsSignedOut(Times.Once());
    }

    [Fact]
    public async Task Handle_Confirm_OverAnAppBoundAfterTheCodeWasSent_FromAPasswordSession_ReturnsReauthenticationRequired_BeforeCounting()
    {
        GivenRow(App(_user.Id));
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");

        var result = await Confirm();

        result.FirstError.Should().Be(AuthErrors.ReauthenticationRequired);
        VerifyNothingCounted();
        _store.Verify(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Confirm_WhenTheRowChangedOnce_TriesAgainAgainstTheRowAsItIsNow()
    {
        GivenRow(null);
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");
        GivenBind(LoginCommitOutcome.FactorLost, LoginCommitOutcome.Committed);

        var result = await Confirm();

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryBindEmailCodeAsync(It.IsAny<EmailCodeBinding>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_Confirm_WhenAnotherRequestSpentTheCodeFirst_ReturnsEmailCodeInvalid_AndRaisesNothing()
    {
        GivenRow(null);
        GivenLive(TwoFactorChallengePurpose.EmailEnrollment, "111111");
        GivenBind(LoginCommitOutcome.ChallengeLost);

        var result = await Confirm();

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeInvalid);
        Events.Should().BeEmpty();
    }

    // ── email/disable ──

    private Task<ErrorOr<DisableEmailCodeResult>> Disable(string? code, bool useRecoveryCode = false) =>
        DisableHandler().Handle(
            new DisableEmailCodeCommand(_user.Id, code, useRecoveryCode, SessionId, IdpCookie, ClientIp), CancellationToken.None);

    [Fact]
    public async Task Handle_Disable_OnAnAccountWhoseFactorIsNotTheEmailedCode_ReturnsNotEnabled()
    {
        GivenRow(App(_user.Id));

        var result = await Disable("111111");

        result.FirstError.Should().Be(UserErrors.TwoFactorNotEnabled);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_Disable_WithoutACode_EmailsTheSwitchOffCode_OnItsOwnChallenge()
    {
        GivenRow(EmailRow(_user.Id));
        TwoFactorChallenge? opened = null;
        _challenges.Setup(c => c.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<TwoFactorChallenge, CancellationToken>((challenge, _) => opened = challenge);

        var result = await Disable(null);

        result.Value.CodeSent.Should().Be(_sent);
        opened!.Purpose.Should().Be(TwoFactorChallengePurpose.EmailDisable);
        opened.PrimaryMethod.Should().Be(AuthenticationMethods.Password);
        _store.Verify(s => s.TryRemoveEmailCodeAsync(It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Disable_WithAnEmailedCodeButNoLiveSwitchOff_AsksForOne_AndCountsNothing()
    {
        GivenRow(EmailRow(_user.Id));

        var result = await Disable("111111");

        result.FirstError.Should().Be(TwoFactorErrors.RecoveryEmailCodeRequired);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_Disable_WithTheEmailedCode_SpendsItAndRemovesTheFactor_RaisesTheEvent_AndSignsTheOtherSessionsOut()
    {
        GivenRow(EmailRow(_user.Id));
        var challenge = GivenLive(TwoFactorChallengePurpose.EmailDisable, "111111");
        SecondFactorProof? spent = null;
        _store.Setup(s => s.TryRemoveEmailCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, SecondFactorProof, CancellationToken>((_, proof, _) => spent = proof)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Disable("111111");

        result.Value.CodeSent.Should().BeNull("the factor was switched off");
        spent!.Method.Should().Be(SecondFactorMethod.EmailCode);
        spent.ChallengeId.Should().Be(challenge.Id);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, 100, It.IsAny<CancellationToken>()), Times.Once,
            "an emailed code is counted on its own counter, under the limit");
        _user.TwoFactorEnabled.Should().BeFalse();
        Events.Should().ContainSingle().Which.Should().BeOfType<TwoFactorEmailCodeDisabledEvent>()
            .Which.Reason.Should().Be(TwoFactorEmailCodeDisableReasons.User);
        VerifyOtherSessionsSignedOut(Times.Once());
    }

    [Fact]
    public async Task Handle_Disable_WithARecoveryCode_SpendsIt()
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]"));
        _store.Setup(s => s.TryRemoveEmailCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Disable("RECOVERY-1", useRecoveryCode: true);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryRemoveEmailCodeAsync(
            _user.Id, It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.RecoveryCode && p.OldCodesJson == "[\"h1\",\"h2\"]"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Disable_WhenTheCodeWasSpentFirst_AsksForANewOne_AndChangesNothing()
    {
        GivenRow(EmailRow(_user.Id));
        GivenLive(TwoFactorChallengePurpose.EmailDisable, "111111");
        _store.Setup(s => s.TryRemoveEmailCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.ChallengeLost);

        var result = await Disable("111111");

        result.FirstError.Should().Be(TwoFactorErrors.RecoveryEmailCodeRequired);
        Events.Should().BeEmpty();
        VerifyOtherSessionsSignedOut(Times.Never());
    }

    // ── The authenticator app in place of the emailed code (OI-220 (2), (6)) ──

    private Task<ErrorOr<EnableTwoFactorResponse>> EnableApp(string? proof = null, bool proofIsRecoveryCode = false, string? bindCode = null) =>
        EnableAppHandler().Handle(
            new EnableTwoFactorCommand(_user.Id, "123456", SessionId, ClientIp, bindCode, IdpCookie, proof, proofIsRecoveryCode),
            CancellationToken.None);

    private void GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow()
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]", protectedSecretKey: AppSecret));
        _protector.Setup(p => p.UnprotectAsync(_user.Id, AppSecret, It.IsAny<CancellationToken>())).ReturnsAsync("PLAIN");
        _totp.Setup(t => t.ValidateCode("PLAIN", "123456")).Returns(Step);
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_WithoutAProof_ReturnsReplacementProofRequired_AndCountsNothing()
    {
        GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow();

        var result = await EnableApp();

        result.FirstError.Should().Be(TwoFactorErrors.ReplacementProofRequired);
        VerifyNothingCounted();
        _totp.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_BeforeSetup_ReturnsSetupRequired()
    {
        GivenRow(EmailRow(_user.Id));

        var result = await EnableApp("111111");

        result.FirstError.Should().Be(TwoFactorErrors.SetupRequired);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_WithAnEmailedProof_SwitchesInOneCommit_WithoutAskingTheMailboxTwice()
    {
        // Fact 3: the emailed proof is the mailbox proof too — the first-factor code is
        // not asked even while its policy is on.
        _twoFactor.RequireEmailCodeForFirstFactor = true;
        GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow();
        var challenge = GivenLive(TwoFactorChallengePurpose.EmailDisable, "111111");
        SecondFactorProof? replaced = null;
        _store.Setup(s => s.TryReplaceEmailCodeWithTotpAsync(
                _user.Id, It.IsAny<SecondFactorProof>(), AppSecret, It.IsAny<string>(), Step, It.IsAny<bool>(), null,
                It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, SecondFactorProof, string, string, long, bool, Guid?, SessionUpgrade, CancellationToken>(
                (_, proof, _, _, _, _, _, _, _) => replaced = proof)
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await EnableApp("111111");

        result.Value.RecoveryCodes.Should().Equal(GeneratedCodes);
        replaced!.Method.Should().Be(SecondFactorMethod.EmailCode);
        replaced.ChallengeId.Should().Be(challenge.Id);
        _store.Verify(s => s.TryEnableAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<bool>(),
            It.IsAny<Guid?>(), It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()), Times.Never);
        Events.Should().HaveCount(2);
        Events[0].Should().BeOfType<TwoFactorEmailCodeDisabledEvent>()
            .Which.Reason.Should().Be(TwoFactorEmailCodeDisableReasons.ReplacedByAuthenticator);
        Events[1].Should().BeOfType<TwoFactorEnabledEvent>();
        VerifyOtherSessionsSignedOut(Times.Once());
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_WithARecoveryCode_StillAsksTheMailboxWhileItsPolicyIsOn()
    {
        // A recovery code proves no mailbox: the first-factor code is still asked,
        // before anything is counted.
        _twoFactor.RequireEmailCodeForFirstFactor = true;
        GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow();

        var result = await EnableApp("RECOVERY-1", proofIsRecoveryCode: true);

        result.FirstError.Should().Be(TwoFactorErrors.EmailCodeRequired);
        VerifyNothingCounted();
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_WithARecoveryCode_SpendsItInTheSwitch()
    {
        GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow();
        _store.Setup(s => s.TryReplaceEmailCodeWithTotpAsync(
                _user.Id, It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.RecoveryCode), AppSecret, It.IsAny<string>(), Step,
                It.IsAny<bool>(), null, It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await EnableApp("RECOVERY-1", proofIsRecoveryCode: true);

        result.IsError.Should().BeFalse();
        _user.TwoFactorEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_EnableApp_OverTheEmailedCode_WhenTheRecoveryCodeWasSpentFirst_ReturnsInvalidRecoveryCode()
    {
        GivenAppCodeChecksAgainstTheSecretSetUpOnTheEmailRow();
        _store.Setup(s => s.TryReplaceEmailCodeWithTotpAsync(
                _user.Id, It.IsAny<SecondFactorProof>(), AppSecret, It.IsAny<string>(), Step, It.IsAny<bool>(), null,
                It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.RecoveryCodesChanged);

        var result = await EnableApp("RECOVERY-1", proofIsRecoveryCode: true);

        result.FirstError.Should().Be(TwoFactorErrors.InvalidRecoveryCode);
        Events.Should().BeEmpty();
        VerifyOtherSessionsSignedOut(Times.Never());
    }

    [Fact]
    public async Task Handle_EnableApp_AFirstBind_SignsNobodyOut()
    {
        // EO8: a first bind revokes nothing; only the replacements and switching off do.
        GivenRow(new TwoFactorSnapshot(_user.Id, AppSecret, null, isEnabled: false, failedAttempts: 0, lockedUntil: null));
        _store.Setup(s => s.HasEnabledFactorAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _protector.Setup(p => p.UnprotectAsync(_user.Id, AppSecret, It.IsAny<CancellationToken>())).ReturnsAsync("PLAIN");
        _totp.Setup(t => t.ValidateCode("PLAIN", "123456")).Returns(Step);
        _store.Setup(s => s.TryEnableAsync(_user.Id, AppSecret, It.IsAny<string>(), Step, It.IsAny<bool>(), null,
                It.IsAny<SessionUpgrade>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await EnableApp();

        result.IsError.Should().BeFalse();
        Events.Should().ContainSingle().Which.Should().BeOfType<TwoFactorEnabledEvent>();
        VerifyOtherSessionsSignedOut(Times.Never());
    }
}
