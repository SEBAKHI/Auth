using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithEmailCode;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Features.Authentication.SignInWithEmailCode;
using Auth.Application.Features.Authentication.StartEmailCodeSignIn;
using Auth.Application.Features.Authentication.StartRegistration;
using Auth.Application.Features.Users.Common;
using Auth.Application.Notifications;
using Auth.Application.Features.Authentication.VerifyEmailCodeSignIn;
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
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Authentication.Commands;

/// <summary>
/// OI-215 T3, T4, T6, T8 (V3–V6, V9): what happens after the code of a sign-in by
/// emailed code. Before a match nothing about the address is read; after it the
/// account decides, and an emailed code proves the mailbox, never the password.
/// </summary>
public class EmailCodeSignInHandlerTests
{
    private const string Handle = "handle:JANE@ONE.EXAMPLE";
    private const string Code = "123456";
    private const string Address = "jane@one.example";

    // ── V3: the verify step ──────────────────────────────────────────────────

    [Theory]
    [InlineData(PendingRegistrationCodeOutcome.Wrong, "EmailVerification.InvalidOrExpiredOtp")]
    [InlineData(PendingRegistrationCodeOutcome.NotFound, "EmailVerification.InvalidOrExpiredOtp")]
    [InlineData(PendingRegistrationCodeOutcome.Exhausted, "EmailVerification.TooManyAttempts")]
    public async Task Verify_WithoutAMatch_AnswersTheCodeErrorAndReadsNothingAboutTheAddress(
        PendingRegistrationCodeOutcome outcome, string code)
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address)).CodeAnswers(outcome);

        var result = await scenario.VerifyAsync();

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be(code);
        scenario.Users.Invocations.Should().BeEmpty("no answer of the account kind before the code matched");
        scenario.Requests.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_ForAnAccount_AnswersSignIn_AndConsumesNothing()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address));

        var result = await scenario.VerifyAsync();

        result.Value.Should().Be(EmailCodeNextStep.SignIn);
        scenario.Pending.Verify(p => p.TryConsumeSignInCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_ForAnAccountWaitingForDeletion_AnswersRecover()
    {
        var scenario = new Scenario().WithAccountWaitingForDeletion(TestHelpers.CreateUser(email: Address, isDeleted: true));

        var result = await scenario.VerifyAsync();

        result.Value.Should().Be(EmailCodeNextStep.Recover);
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, false, true, false, false)]
    // With email off the verify step refuses before the code (Verify_WithEmailOff_RefusesBeforeTheCodeCheck);
    // the sign-up then asks a password whatever the switch says: CompleteRegistration's tests.
    public async Task Verify_ForAFreeAddress_AnswersTheFieldsTheSettingsAsk(
        bool passwordAtSignUp, bool nameAtSignUp, bool emailEnabled, bool passwordRequired, bool nameRequired)
    {
        var scenario = new Scenario();
        scenario.Registration.PasswordAtSignUp = passwordAtSignUp;
        scenario.Registration.NameAtSignUp = nameAtSignUp;
        scenario.Email.Enabled = emailEnabled;

        var result = await scenario.VerifyAsync();

        result.Value.Step.Should().Be(EmailCodeSteps.CompleteAccount);
        result.Value.PasswordRequired.Should().Be(passwordRequired,
            "the password switch is honoured while email is on");
        result.Value.NameRequired.Should().Be(nameRequired);
    }

    [Fact]
    public async Task Verify_ForAFreeAddressWithSignUpClosed_RefusesAfterTheMatch()
    {
        var scenario = new Scenario();
        scenario.Registration.AllowSelfRegistration = false;

        var result = await scenario.VerifyAsync();

        result.FirstError.Code.Should().Be(UserErrors.SelfRegistrationClosed.Code);
    }

    // ── REVIEW 110 F1 (a): email off, no code step at all ────────────────────

    [Fact]
    public async Task Verify_WithEmailOff_RefusesBeforeTheCodeCheck()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address));
        scenario.Email.Enabled = false;

        var result = await scenario.VerifyAsync();

        result.FirstError.Code.Should().Be(UserErrors.EmailCodeSignInUnavailable.Code);
        scenario.Pending.Invocations.Should().BeEmpty("no row is read and no attempt charged while no code can have reached anyone");
        scenario.Users.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_WithEmailOff_RefusesBeforeTheCodeCheck()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address, passwordHash: null));
        scenario.Email.Enabled = false;

        var result = await scenario.SignInAsync();

        result.FirstError.Code.Should().Be(UserErrors.EmailCodeSignInUnavailable.Code);
        scenario.Pending.Verify(p => p.CheckCodeUnderLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        scenario.Pending.Invocations.Should().BeEmpty();
        scenario.Builder.Invocations.Should().BeEmpty("a code minted by a start that sent nothing must open nothing");
    }

    [Fact]
    public async Task Recover_WithEmailOff_RefusesBeforeTheCodeCheck()
    {
        var scenario = new Scenario().WithAccountWaitingForDeletion(TestHelpers.CreateUser(email: Address, isDeleted: true, passwordHash: null));
        scenario.Email.Enabled = false;

        var result = await scenario.RecoverAsync();

        result.FirstError.Code.Should().Be(UserErrors.EmailCodeSignInUnavailable.Code);
        scenario.Pending.Invocations.Should().BeEmpty();
        scenario.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── REVIEW 110 F2: the address's wrong-code ceiling ──────────────────────

    [Fact]
    public async Task EveryCodeStep_AtTheAddresssCeiling_AnswersItsOwnCode_AndReadsNothingAboutTheAddress()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address, passwordHash: null))
            .CodeAnswers(PendingRegistrationCodeOutcome.WrongCodeCeilingReached);

        var verify = await scenario.VerifyAsync();
        var signIn = await scenario.SignInAsync();
        var recover = await scenario.RecoverAsync();

        verify.FirstError.Code.Should().Be(EmailVerificationErrors.WrongCodeLimitReached.Code);
        signIn.FirstError.Code.Should().Be(EmailVerificationErrors.WrongCodeLimitReached.Code);
        recover.FirstError.Code.Should().Be(EmailVerificationErrors.WrongCodeLimitReached.Code);
        scenario.Users.Invocations.Should().BeEmpty("the ceiling is the address's, the same whether or not it has an account");
        scenario.Consumed.Should().Be(0);
    }

    // ── REVIEW 110 F3: one usable code, whichever start minted it ────────────

    [Fact]
    public async Task ASignUpStartThenASignInStart_LeaveTheOwnerOneSignInCode_ThatSignsIn()
    {
        // The registration's start used to mint a code for an address with an
        // account and send only a notice; the sign-in start then found that live,
        // mailed row and sent nothing, so the owner had no usable code for its life.
        var user = TestHelpers.CreateUser(email: Address, passwordHash: null);
        var store = new InMemoryPendingRegistrations();
        var starts = new StartFixture(user);
        var scenario = new Scenario { PendingStore = store }.WithAccount(user);

        var signUp = await starts.RegistrationStart(store).Handle(
            new StartRegistrationCommand(Address, "en", "203.0.113.7", "TestAgent/1.0"), CancellationToken.None);
        var signIn = await starts.SignInStart(store).Handle(
            new StartEmailCodeSignInCommand(Address, "en", "203.0.113.7", "TestAgent/1.0"), CancellationToken.None);

        signUp.Value.PendingId.Should().Be(Handle);
        signIn.Value.PendingId.Should().Be(Handle);
        var message = starts.Sent.Should().ContainSingle("the second start finds the live, mailed code and sends nothing more").Subject;
        message.TypeCode.Should().Be(NotificationTypeCodes.EmailSignInCode, "the code in the mailbox is one its holder can use");
        message.Variables["OtpCode"].Should().Be(Code);

        var result = await scenario.SignInAsync();

        result.IsError.Should().BeFalse();
        result.Value.Token.Should().NotBeNull("the code the sign-up start mailed signs in");
        store.Row!.IsConsumed.Should().BeTrue("and it signs in once");
    }

    // ── V4: the sign-in exit ─────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_PasswordlessAccount_IssuesASessionWithTheFirstFactorBit()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address, passwordHash: null));

        var result = await scenario.SignInAsync(staySignedIn: true);

        result.IsError.Should().BeFalse();
        scenario.BuiltMethods.Should().Be(AuthenticationMethods.EmailCode.With(AuthenticationMethods.EmailCodeFirstFactor));
        scenario.BuiltMethods!.Value.HasPrimary.Should().BeTrue();
        scenario.PasswordProven.Should().BeFalse("an emailed code proves the mailbox, never the password (G8)");
        scenario.Persistent.Should().BeTrue("the sign-in by code carries the person's choice");
        scenario.Consumed.Should().Be(1);
    }

    [Fact]
    public async Task SignIn_PasswordAccount_RecordsTheEmailedCodeOnly()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address));

        await scenario.SignInAsync();

        scenario.BuiltMethods.Should().Be(AuthenticationMethods.EmailCode);
        scenario.BuiltMethods!.Value.HasPrimary.Should().BeFalse("a password account's code is never a factor (D-215-1)");
    }

    [Fact]
    public async Task SignIn_PasswordAccountHoldingPlatformAuthority_AsksForThePassword_WhateverTheEnforcementSwitch()
    {
        // The handler never reads TwoFactor:EnforceForPlatformAdmins: keyed on it, a
        // mailbox holder would sign in to a platform administrator whenever it is off.
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address)).WithPlatformPermission();

        var result = await scenario.SignInAsync();

        result.Value.NextStep.Should().Be(EmailCodeNextStep.PasswordRequiredToFinish);
        result.Value.Token.Should().BeNull("no token before the password");
        scenario.Builder.Invocations.Should().BeEmpty();
        scenario.Challenges.Verify(c => c.CreateChallengeAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<AuthenticationMethods>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheFullProofRule_CannotReadTheEnforcementSwitch()
    {
        // REVIEW 110 F11: the test above never sets the switch, so its name alone
        // proves nothing. The rule is handed no settings at all: keyed on
        // TwoFactor:EnforceForPlatformAdmins, a mailbox holder would sign in to a
        // platform administrator whenever the switch is off.
        var parameters = typeof(EmailCodeFullProofRule).GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToList();

        parameters.Should().NotBeEmpty();
        parameters.Should().OnlyContain(type => type == typeof(ITokenClaimsResolver),
            "the platform claims are the rule's only input");
    }

    [Fact]
    public async Task SignIn_PasswordAccountAskedForFullProof_AsksForThePassword()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address));

        var result = await scenario.SignInAsync(requireFullProof: true);

        result.Value.NextStep.Should().Be(EmailCodeNextStep.PasswordRequiredToFinish);
        scenario.Builder.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_PasswordlessAccountAskedForFullProof_SignsIn_ForItHasNoPasswordToGive()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address, passwordHash: null)).WithPlatformPermission();

        var result = await scenario.SignInAsync(requireFullProof: true);

        result.Value.NextStep.Should().BeNull();
        scenario.BuiltMethods.Should().Be(AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SignIn_WithTheAuthenticatorApp_OpensItsChallenge_WhoseCompletionCountsOnlyWithoutAPassword(
        bool hasPassword, bool completionIsTwoFactor)
    {
        var user = TestHelpers.CreateUser(email: Address, twoFactorEnabled: true, passwordHash: hasPassword ? "TestPasswordHash" : null);
        var scenario = new Scenario().WithAccount(user);

        var result = await scenario.SignInAsync();

        result.Value.RequiresTwoFactor.Should().BeTrue();
        scenario.ChallengeMethods.Should().Be(AuthenticationMethods.ForEmailCodeSignIn(hasPassword));
        scenario.ChallengeMethods!.Value.With(AuthenticationMethods.Totp).IsMfaSatisfied.Should().Be(completionIsTwoFactor,
            "the app completes two factors only for an account whose code is its first factor");
        scenario.Builder.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_WhoseSecondFactorIsTheEmailedCode_AsksForNoSecondStep_AndSaysSo()
    {
        var user = TestHelpers.CreateUser(email: Address, twoFactorEnabled: true);
        var scenario = new Scenario().WithAccount(user);
        scenario.Challenges.Setup(c => c.SignInCodeProvesFactorAsync(user, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await scenario.SignInAsync();

        result.Value.RequiresTwoFactor.Should().BeFalse();
        scenario.BuiltMethods.Should().Be(AuthenticationMethods.EmailCode);
        scenario.Logger.Entries.Should().Contain(e => e.Message.Contains(TwoFactorSignalCodes.SignInCodeMailboxWaived) && e.Message.Contains(user.Id.ToString()));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    public async Task SignIn_UnderTheHardOrTheAdministratorsLock_IsRefused_AndSpendsNothing(int failedAttempts)
    {
        var user = TestHelpers.CreateUser(email: Address, status: UserStatus.Locked, failedLoginAttempts: failedAttempts, lockoutEnd: null);
        var scenario = new Scenario().WithAccount(user).Familiar(true);

        var result = await scenario.SignInAsync();

        result.IsError.Should().BeTrue();
        scenario.Consumed.Should().Be(0);
        scenario.Builder.Invocations.Should().BeEmpty();
        scenario.Lockout.Verify(l => l.ClearAutomaticLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SignIn_UnderATimedLock_AdmitsTheOwnersDeviceOnly_AndLiftsNothing(bool familiar, bool refused)
    {
        var user = TestHelpers.CreateUser(email: Address, status: UserStatus.Locked, failedLoginAttempts: 5,
            lockoutEnd: DateTime.UtcNow.AddMinutes(10));
        var scenario = new Scenario().WithAccount(user).Familiar(familiar);

        var result = await scenario.SignInAsync();

        result.IsError.Should().Be(refused);
        scenario.PasswordProven.Should().NotBe(true, "a code lifts no lock (G8)");
        scenario.Lockout.Verify(l => l.ClearAutomaticLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignIn_ForNoAccountOrOneWaitingForDeletion_AnswersTheNextStep_AndSpendsNothing(bool waitingForDeletion)
    {
        var scenario = waitingForDeletion
            ? new Scenario().WithAccountWaitingForDeletion(TestHelpers.CreateUser(email: Address, isDeleted: true))
            : new Scenario();

        var result = await scenario.SignInAsync();

        result.Value.NextStep!.Step.Should().Be(waitingForDeletion ? EmailCodeSteps.Recover : EmailCodeSteps.CompleteAccount);
        scenario.Consumed.Should().Be(0);
        scenario.Builder.Invocations.Should().BeEmpty();
    }

    // ── V5: single use ───────────────────────────────────────────────────────

    [Fact]
    public async Task SignIn_WhenTheCodeWasAlreadySpent_IsRefused_AndIssuesNothing()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address, passwordHash: null)).ConsumeAnswers(false);

        var result = await scenario.SignInAsync();

        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
        scenario.Builder.Invocations.Should().BeEmpty("a code signs in once");
    }

    // ── V6: the first confirmation ───────────────────────────────────────────

    [Fact]
    public async Task SignIn_ConfirmingTheAddressForTheFirstTime_RetiresAPasswordSetBeforeIt()
    {
        var user = TestHelpers.CreateUser(email: Address, emailConfirmed: false);
        user.RequirePasswordChange(Guid.NewGuid());
        var scenario = new Scenario().WithAccount(user);

        var result = await scenario.SignInAsync();

        result.IsError.Should().BeFalse();
        scenario.Users.Verify(u => u.ConfirmEmailBySignInCodeAsync(user.Id, "TestPasswordHash", It.IsAny<CancellationToken>()), Times.Once);
        user.EmailConfirmed.Should().BeTrue();
        user.PasswordHash.Should().BeNull();
        user.MustChangePassword.Should().BeFalse("a forced change would ask for a password that no longer exists");
        scenario.Revocation.Verify(r => r.RevokeCredentialsAsync(user.Id, null, null, user.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        scenario.DispatchedEvents.OfType<PasswordRemovedEvent>().Should().ContainSingle()
            .Which.Reason.Should().Be(PasswordRemovalReasons.SetBeforeAddressProven);
        scenario.BuiltMethods.Should().Be(AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false),
            "the sign-in continues as a passwordless one");
    }

    [Fact]
    public async Task SignIn_ConfirmingAnAddressWithoutAPassword_RevokesNothing()
    {
        var user = TestHelpers.CreateUser(email: Address, emailConfirmed: false, passwordHash: null);
        var scenario = new Scenario().WithAccount(user);

        await scenario.SignInAsync();

        user.EmailConfirmed.Should().BeTrue();
        scenario.Revocation.Invocations.Should().BeEmpty();
        scenario.DispatchedEvents.OfType<PasswordRemovedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task SignIn_ForAConfirmedAddress_TouchesNeitherThePasswordNorTheCredentials()
    {
        var user = TestHelpers.CreateUser(email: Address);
        var scenario = new Scenario().WithAccount(user);

        await scenario.SignInAsync();

        user.PasswordHash.Should().Be("TestPasswordHash");
        scenario.Users.Verify(u => u.ConfirmEmailBySignInCodeAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        scenario.Revocation.Invocations.Should().BeEmpty();
    }

    // ── V9: the grace period ─────────────────────────────────────────────────

    [Fact]
    public async Task Recover_PasswordlessAccountWaitingForDeletion_RecoversByCode()
    {
        var user = TestHelpers.CreateUser(email: Address, isDeleted: true, passwordHash: null);
        var scenario = new Scenario().WithAccountWaitingForDeletion(user);

        var result = await scenario.RecoverAsync();

        result.IsError.Should().BeFalse();
        scenario.Consumed.Should().Be(1);
        scenario.Users.Verify(u => u.RestoreAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        scenario.BuiltMethods.Should().Be(AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false));
        scenario.PasswordProven.Should().BeFalse();
    }

    [Fact]
    public async Task Recover_WithTheAuthenticatorAppAndNoCode_AsksForIt_AndKeepsTheEmailedCode()
    {
        var user = TestHelpers.CreateUser(email: Address, isDeleted: true, passwordHash: null, twoFactorEnabled: true);
        var scenario = new Scenario().WithAccountWaitingForDeletion(user);

        var result = await scenario.RecoverAsync();

        result.FirstError.Code.Should().Be(UserErrors.TwoFactorRequired.Code);
        scenario.Consumed.Should().Be(0, "a person who has not typed the app's code keeps the emailed one");
    }

    [Fact]
    public async Task Recover_WhenTheAuthenticatorCodeIsRefused_KeepsTheEmailedCode()
    {
        // REVIEW 110 F9: the emailed code is spent only after the app's code was
        // accepted, so a mistyped app code needs no new email.
        var user = TestHelpers.CreateUser(email: Address, isDeleted: true, passwordHash: null, twoFactorEnabled: true);
        var scenario = new Scenario().WithAccountWaitingForDeletion(user);
        scenario.SecondFactors.Setup(v => v.ReserveAsync(user.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoFactorErrors.LockedOut);
        scenario.SecondFactors.Setup(v => v.ReserveUnderTheTimedLockOnlyAsync(user.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoFactorErrors.LockedOut);

        var result = await scenario.RecoverAsync(twoFactorCode: "654321");

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        scenario.Consumed.Should().Be(0, "the app's code was refused, so the emailed one is still the person's");
        scenario.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recover_WhenTheEmailedCodeWasSpentInBetween_RestoresNothing()
    {
        var user = TestHelpers.CreateUser(email: Address, isDeleted: true, passwordHash: null);
        var scenario = new Scenario().WithAccountWaitingForDeletion(user).ConsumeAnswers(false);

        var result = await scenario.RecoverAsync();

        result.FirstError.Code.Should().Be(EmailVerificationErrors.InvalidOrExpiredOtp.Code);
        scenario.Consumed.Should().Be(1);
        scenario.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        scenario.Requests.Verify(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recover_ForALiveAccount_AnswersSignIn_AndSpendsNothing()
    {
        var scenario = new Scenario().WithAccount(TestHelpers.CreateUser(email: Address));

        var result = await scenario.RecoverAsync();

        result.Value.NextStep.Should().Be(EmailCodeNextStep.SignIn);
        scenario.Consumed.Should().Be(0);
    }

    [Fact]
    public async Task Recover_PasswordAccountHoldingPlatformAuthority_ContinuesWithThePasswordEntry()
    {
        var user = TestHelpers.CreateUser(email: Address, isDeleted: true);
        var scenario = new Scenario().WithAccountWaitingForDeletion(user).WithPlatformPermission();

        var result = await scenario.RecoverAsync();

        result.Value.NextStep.Should().Be(EmailCodeNextStep.PasswordRequiredToFinish);
        scenario.Consumed.Should().Be(0);
    }

    // ── Scenario ─────────────────────────────────────────────────────────────

    private sealed class Scenario
    {
        public Mock<IPendingRegistrationRepository> Pending { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IAccountDeletionRequestRepository> Requests { get; } = new();
        public Mock<IAccountLockout> Lockout { get; } = new();
        public Mock<ILoginAttemptRepository> Attempts { get; } = new();
        public Mock<ITokenClaimsResolver> Claims { get; } = new();
        public Mock<ITwoFactorChallengeService> Challenges { get; } = new();
        public Mock<ICredentialRevocationService> Revocation { get; } = new();
        public Mock<ILoginResponseBuilder> Builder { get; } = new();
        public Mock<IDomainEventDispatcher> Events { get; } = new();
        public Mock<ISecondFactorVerifier> SecondFactors { get; } = new();

        /// <summary>A stateful store in place of <see cref="Pending"/>, when a test needs one.</summary>
        public IPendingRegistrationRepository? PendingStore { get; init; }

        private IPendingRegistrationRepository PendingRepository => PendingStore ?? Pending.Object;
        public CapturingLogger<SignInWithEmailCodeCommandHandler> Logger { get; } = new();
        public RegistrationSettings Registration { get; } = new();
        public EmailSettings Email { get; } = new() { Enabled = true };

        public AuthenticationMethods? BuiltMethods { get; private set; }
        public AuthenticationMethods? ChallengeMethods { get; private set; }
        public bool? PasswordProven { get; private set; }
        public bool? Persistent { get; private set; }
        public int Consumed { get; private set; }
        public List<object> DispatchedEvents { get; } = [];

        private readonly PendingRegistration _row;
        private PendingRegistrationCodeOutcome _outcome = PendingRegistrationCodeOutcome.Match;
        private bool _consume = true;
        private User? _live;
        private User? _deleted;
        private AccountDeletionRequest? _request;
        private IReadOnlyList<string> _permissions = [];

        public Scenario()
        {
            var now = DateTime.UtcNow;
            _row = PendingRegistration.Create(Handle, Address, now);
            _row.TryChargeMailWindow(TimeSpan.FromSeconds(60), 3, now);
            _row.IssueCode("hash-of-123456", 5, "en", now);

            Pending.Setup(p => p.CheckCodeUnderLockAsync(Handle, Code, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new PendingRegistrationCodeCheck(_outcome,
                    _outcome == PendingRegistrationCodeOutcome.NotFound ? null : _row));
            Pending.Setup(p => p.TryConsumeSignInCodeAsync(_row.Id, Code, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Consumed++;
                    return _consume;
                });

            Users.Setup(u => u.GetByEmailAsync(Address, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live);
            Users.Setup(u => u.GetByEmailIncludeDeletedAsync(Address, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live ?? _deleted);
            Users.Setup(u => u.ConfirmEmailBySignInCodeAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Users.Setup(u => u.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _deleted is null ? _live : Restored());
            Requests.Setup(r => r.GetActiveByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _request);
            Requests.Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

            Claims.Setup(c => c.ResolveAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new TokenClaims([], _permissions, []));

            Challenges.Setup(c => c.CreateChallengeAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<AuthenticationMethods>(), It.IsAny<CancellationToken>()))
                .Callback<User, string?, string?, AuthenticationMethods, CancellationToken>((_, _, _, methods, _) => ChallengeMethods = methods)
                .ReturnsAsync(new IssuedTwoFactorChallenge("challenge", [TwoFactorMethodNames.Totp, TwoFactorMethodNames.Recovery]));

            Builder.Setup(b => b.BuildAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<AuthenticationMethods>(), It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(),
                    It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(),
                    It.IsAny<Guid?>(), It.IsAny<bool>()))
                .Callback(new InvocationAction(invocation =>
                {
                    BuiltMethods = (AuthenticationMethods)invocation.Arguments[4];
                    PasswordProven = (bool)invocation.Arguments[5];
                    Persistent = (bool)invocation.Arguments[12];
                }))
                .ReturnsAsync(new LoginResponse
                {
                    Token = new TokenResponse { AccessToken = "token", RefreshToken = "refresh", ExpiresIn = 900, RefreshExpiresIn = 3600 }
                });

            Events.Setup(e => e.DispatchEventsAsync(It.IsAny<Auth.Domain.Primitives.AggregateRoot>(), It.IsAny<CancellationToken>()))
                .Callback<Auth.Domain.Primitives.AggregateRoot, CancellationToken>((root, _) =>
                {
                    DispatchedEvents.AddRange(root.DomainEvents);
                    root.ClearDomainEvents();
                })
                .Returns(Task.CompletedTask);
        }

        public Scenario CodeAnswers(PendingRegistrationCodeOutcome outcome) { _outcome = outcome; return this; }
        public Scenario ConsumeAnswers(bool consumed) { _consume = consumed; return this; }
        public Scenario WithAccount(User user) { _live = user; return this; }
        public Scenario WithPlatformPermission() { _permissions = ["users:read"]; return this; }

        public Scenario WithAccountWaitingForDeletion(User user)
        {
            _deleted = user;
            _request = AccountDeletionRequest.Create(user.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", user.Id);
            return this;
        }

        public Scenario Familiar(bool familiar)
        {
            Attempts.Setup(a => a.HasSucceededFromAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(familiar);
            return this;
        }

        public Task<ErrorOr<EmailCodeNextStep>> VerifyAsync() =>
            new VerifyEmailCodeSignInCommandHandler(CodeCheck(), Accounts())
                .Handle(new VerifyEmailCodeSignInCommand(Handle, Code), CancellationToken.None);

        public Task<ErrorOr<LoginResponse>> SignInAsync(bool requireFullProof = false, bool staySignedIn = false) =>
            new SignInWithEmailCodeCommandHandler(
                CodeCheck(), Accounts(), PendingRepository, Users.Object, Lockout.Object, Attempts.Object,
                new EmailCodeFullProofRule(Claims.Object), Challenges.Object, Revocation.Object, Builder.Object,
                Events.Object, Logger)
                .Handle(new SignInWithEmailCodeCommand(Handle, Code, requireFullProof, staySignedIn, "device", "203.0.113.7", "TestAgent/1.0"),
                    CancellationToken.None);

        public Task<ErrorOr<LoginResponse>> RecoverAsync(string? twoFactorCode = null) =>
            new RecoverAccountWithEmailCodeCommandHandler(
                CodeCheck(), Accounts(), PendingRepository, new EmailCodeFullProofRule(Claims.Object), Challenges.Object,
                Recoverer())
                .Handle(new RecoverAccountWithEmailCodeCommand(Handle, Code, twoFactorCode, false, "203.0.113.7", "TestAgent/1.0", "device"),
                    CancellationToken.None);

        private EmailCodeCheck CodeCheck() => new(PendingRepository, TestHelpers.CreateOptions(Email), new Mock<ILogger<EmailCodeCheck>>().Object);

        private EmailCodeAccountResolver Accounts() => new(
            Users.Object, Requests.Object, TestHelpers.CreateOptions(Registration), TestHelpers.CreateOptions(Email));

        private Auth.Application.Features.AccountDeletion.Common.AccountDeletionRecoverer Recoverer() => new(
            Requests.Object,
            Users.Object,
            SecondFactors.Object,
            new Mock<ITwoFactorStateStore>().Object,
            new Mock<ITwoFactorChallengeRepository>().Object,
            new Mock<IEmailCodeSender>().Object,
            new Mock<ICredentialHasher>().Object,
            new Mock<IJwtTokenService>().Object,
            new Mock<ISecondFactorLockout>().Object,
            Builder.Object,
            new Mock<MediatR.IPublisher>().Object,
            new Mock<ILogger<Auth.Application.Features.AccountDeletion.Common.AccountDeletionRecoverer>>().Object);

        private User Restored() => TestHelpers.CreateUser(id: _deleted!.Id, email: Address, passwordHash: _deleted.PasswordHash);
    }

    /// <summary>The two starts, over one store, for an address that has <c>user</c>'s account.</summary>
    private sealed class StartFixture
    {
        private readonly Mock<IUserRepository> _users = new();
        private readonly Mock<IAccountDeletionTombstoneRepository> _tombstones = new();
        private readonly Mock<INotificationService> _notifications = new();
        private readonly Mock<IPendingRegistrationHandle> _handle = new();
        private readonly Mock<IIdentifierHasher> _hasher = new();
        private readonly EmailSettings _email = new() { Enabled = true, OtpExpirationMinutes = 5, RateLimitWindowSeconds = 60, MaxOtpRequestsPerWindow = 3 };
        private readonly RegistrationSettings _registration = new() { AllowSelfRegistration = true };

        public List<NotificationRequest> Sent { get; } = [];

        public StartFixture(User user)
        {
            _users.Setup(u => u.GetNotificationIdentityByEmailAsync(Address, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserNotificationIdentity(user.Id, user.DisplayName, "en", IsDeleted: false));
            _handle.Setup(h => h.For(It.IsAny<string>())).Returns<string>(key => "handle:" + key);
            _hasher.Setup(h => h.HashEmail(It.IsAny<string>())).Returns<string>(email => "hash:" + email);
            _notifications.Setup(n => n.SendAsync(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
                .Callback<NotificationRequest, CancellationToken>((request, _) => Sent.Add(request))
                .ReturnsAsync(Result.Success);
        }

        public StartRegistrationCommandHandler RegistrationStart(IPendingRegistrationRepository store) => new(
            store, _handle.Object, _users.Object, new IdentifierReservationGuard(_tombstones.Object, _hasher.Object),
            _notifications.Object, new Mock<IDomainEventDispatcher>().Object,
            new EmailCodeStartMessages(TestHelpers.CreateOptions(_email)),
            TestHelpers.CreateOptions(_registration), TestHelpers.CreateOptions(_email),
            new Mock<IEnvironmentInfo>().Object, new Mock<ILogger<StartRegistrationCommandHandler>>().Object);

        public StartEmailCodeSignInCommandHandler SignInStart(IPendingRegistrationRepository store) => new(
            store, _handle.Object, _users.Object, new IdentifierReservationGuard(_tombstones.Object, _hasher.Object),
            _notifications.Object, new Mock<IDomainEventDispatcher>().Object,
            new EmailCodeStartMessages(TestHelpers.CreateOptions(_email)),
            TestHelpers.CreateOptions(_registration), TestHelpers.CreateOptions(_email),
            new Mock<IEnvironmentInfo>().Object, new Mock<ILogger<StartEmailCodeSignInCommandHandler>>().Object);
    }

    /// <summary>
    /// One address's pending row with the repository's rules (a live, mailed code is
    /// left alone; a dead one rotates in place), minting <see cref="Code"/>. The locks
    /// are the real repository's business: its own tests, and the REVIEW 110 (F7) race
    /// runs on a real engine.
    /// </summary>
    private sealed class InMemoryPendingRegistrations : IPendingRegistrationRepository
    {
        public PendingRegistration? Row { get; private set; }

        public Task<PendingRegistrationStart> StartAsync(PendingRegistrationStartRequest request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (Row is null)
            {
                Row = PendingRegistration.Create(request.Handle, request.Email, now);
                Row.TryChargeMailWindow(request.MailWindow, request.MaxMailsPerWindow, now);
            }
            else if ((Row.IsCodeLive(now) && Row.WasMailed) || !Row.TryChargeMailWindow(request.MailWindow, request.MaxMailsPerWindow, now))
            {
                return Task.FromResult(new PendingRegistrationStart(Row, PendingRegistrationStartAction.Unchanged, null));
            }

            Row.IssueCode("hash:" + Code, request.ExpirationMinutes, request.PreferredLanguage, now);
            return Task.FromResult(new PendingRegistrationStart(Row, PendingRegistrationStartAction.Minted, Code));
        }

        public Task<PendingRegistrationCodeCheck> CheckCodeUnderLockAsync(string handle, string code, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (Row is null || Row.Handle != handle || Row.IsConsumed || Row.IsExpired(now))
            {
                return Task.FromResult(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.NotFound, null));
            }

            if (Row.IsExhausted)
            {
                return Task.FromResult(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Exhausted, Row));
            }

            if (code != Code)
            {
                Row.RecordFailedAttempt(now);
                return Task.FromResult(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Wrong, Row));
            }

            return Task.FromResult(new PendingRegistrationCodeCheck(PendingRegistrationCodeOutcome.Match, Row));
        }

        public Task MarkMailedAsync(Guid id, string otpHash, CancellationToken cancellationToken)
        {
            if (Row?.OtpHash == otpHash)
            {
                Row.MarkMailed(DateTime.UtcNow);
            }

            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeSignInCodeAsync(Guid id, string code, CancellationToken cancellationToken)
        {
            if (Row is null || Row.Id != id || Row.IsConsumed || Row.IsExpired(DateTime.UtcNow) || code != Code)
            {
                return Task.FromResult(false);
            }

            Row.Consume(DateTime.UtcNow);
            return Task.FromResult(true);
        }

        public Task<int> ConsumeByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> CleanupExpiredAsync(DateTime olderThanUtc, int batchSize, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
