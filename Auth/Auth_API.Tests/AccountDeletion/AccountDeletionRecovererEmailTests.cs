using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccount;
using Auth.Application.Features.AccountDeletion.RecoverAccountExternal;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.AccountDeletion;

/// <summary>
/// The recovery of an account waiting for deletion when its second factor is the emailed
/// code, and a recovery code on either factor (S24, EO9, C5): what has a recovery code's
/// shape is a recovery code, anything else the account's own factor; the emailed code
/// belongs to the password entry alone.
/// </summary>
public class AccountDeletionRecovererEmailTests
{
    private const string RecoveryCode = "ABCD-EFGH";

    private readonly User _user = TestHelpers.CreateUser(twoFactorEnabled: true, isDeleted: true);
    private readonly Mock<IAccountDeletionRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITwoFactorChallengeRepository> _challenges = new();
    private readonly Mock<IEmailCodeSender> _sender = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ILoginResponseBuilder> _builder = new();
    private AuthenticationMethods? _signedInWith;

    public AccountDeletionRecovererEmailTests()
    {
        _requests.Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _users.Setup(u => u.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestHelpers.CreateUser(id: _user.Id));
        _builder
            .Setup(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AuthenticationMethods>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()))
            .Callback<User, string?, string?, string?, AuthenticationMethods, bool, CancellationToken, bool, string?, Guid?, string?, Guid?, bool, Guid?, bool>(
                (_, _, _, _, methods, _, _, _, _, _, _, _, _, _, _) => _signedInWith = methods)
            .ReturnsAsync(new LoginResponse());
        _totp.Setup(t => t.VerifyRecoveryCode(RecoveryCode, "h1")).Returns(true);
    }

    private AccountDeletionRecoverer Recoverer() => new(
        _requests.Object,
        _users.Object,
        new SecondFactorVerifier(
            _store.Object,
            [
                new TotpProofStrategy(_totp.Object, Mock.Of<ITwoFactorSecretProtector>()),
                new RecoveryCodeProofStrategy(_totp.Object),
                new EmailCodeProofStrategy(ScopedOtpHasher.Instance)
            ],
            TestHelpers.CreateSecondFactorLockout()),
        _store.Object,
        _challenges.Object,
        _sender.Object,
        new FakeCredentialHasher(),
        Mock.Of<IJwtTokenService>(j => j.GenerateRefreshToken() == "unhanded-token"),
        TestHelpers.CreateSecondFactorLockout(),
        new TotpReplayPolicy(TestHelpers.CreateOptions(new TwoFactorSettings())),
        _builder.Object,
        Mock.Of<IPublisher>(),
        NullLogger<AccountDeletionRecoverer>.Instance);

    private static AccountDeletionRequest Request(AccountDeletionSource source = AccountDeletionSource.PublicWeb) =>
        AccountDeletionRequest.Create(Guid.NewGuid(), source, TimeSpan.FromDays(30), "2026.07", Guid.NewGuid());

    private void GivenRow(TwoFactorSnapshot row)
    {
        _store.Setup(s => s.GetSnapshotAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _store.Setup(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private TwoFactorChallenge GivenRecoveryChallenge(params string[] codes)
    {
        var challenge = ChallengeWith(_user.Id, AuthenticationMethods.Password, TwoFactorChallengePurpose.DeletionRecovery, codes);
        _challenges.Setup(c => c.GetLiveForUserAsync(_user.Id, TwoFactorChallengePurpose.DeletionRecovery, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        _challenges.Setup(c => c.TryReserveAttemptAsync(
                challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, TwoFactorChallenge.MaxAttempts, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _store.Setup(s => s.TryCommitChallengeAsync(
                challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, _user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        return challenge;
    }

    [Fact]
    public async Task ThePasswordEntry_WithoutACode_AsksForTheEmailedCode()
    {
        GivenRow(EmailRow(_user.Id));

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, null, null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.RecoveryEmailCodeRequired);
    }

    [Fact]
    public async Task TheProviderEntry_WithoutACode_AsksForTheCodeItAlwaysAskedFor()
    {
        GivenRow(EmailRow(_user.Id));

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.ExternalIdentity, null, null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.TwoFactorRequired);
    }

    [Fact]
    public async Task ThePasswordEntry_RecoversWithTheEmailedCode_AndSpendsIt_BeforeTheRequestIsCancelled()
    {
        GivenRow(EmailRow(_user.Id));
        var challenge = GivenRecoveryChallenge("111111");
        var request = Request();

        // The order the name claims (OI-220 (10)): each step records itself.
        var order = new List<string>();
        _store.Setup(s => s.TryCommitChallengeAsync(
                challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, _user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("spent"))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        _requests.Setup(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("cancelled"))
            .ReturnsAsync(true);

        var result = await Recoverer().RecoverAsync(_user, request, AuthenticationMethods.Password, "111111", null, null, null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _signedInWith.Should().Be(AuthenticationMethods.Password.With(AuthenticationMethods.EmailCode));
        _signedInWith!.Value.IsMfaSatisfied.Should().BeFalse();
        _store.Verify(s => s.TryCommitChallengeAsync(
            challenge.Id, TwoFactorChallengePurpose.DeletionRecovery, _user.Id,
            It.Is<SecondFactorProof>(p => p.Method == SecondFactorMethod.EmailCode), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
        order.Should().Equal(["spent", "cancelled"],
            "an accepted code is spent before the request is cancelled, so it cannot be presented again");
    }

    /// <summary>
    /// OI-220 (10): the recovery-code branch's limit, pinned by behaviour rather than
    /// by a count of names. Under the consecutive-failure limit, except for a deletion
    /// the account's own session asked for (X12's exception), on either factor. A
    /// branch swapped in the recoverer turns this red.
    /// </summary>
    [Theory]
    [InlineData(AccountDeletionSource.InApp, false, null)]
    [InlineData(AccountDeletionSource.PublicWeb, false, 100)]
    [InlineData(AccountDeletionSource.InApp, true, null)]
    [InlineData(AccountDeletionSource.PublicWeb, true, 100)]
    public async Task ARecoveryCode_IsUnderTheLimit_UnlessTheAccountsOwnSessionAskedForTheDeletion(
        AccountDeletionSource source, bool emailFactor, int? expectedLimit)
    {
        GivenRow(emailFactor
            ? EmailRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]")
            : TotpRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]"));
        _store.Setup(s => s.TrySpendRecoveryCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Recoverer().RecoverAsync(_user, Request(source), AuthenticationMethods.Password, RecoveryCode, null, null, null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _store.Verify(s => s.TryReserveAttemptAsync(_user.Id, expectedLimit, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryReserveAttemptAsync(_user.Id, It.Is<int?>(limit => limit != expectedLimit), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// OI-220 (2) (a), the recovery leg: a setup may write an authenticator secret onto
    /// an emailed-code row before the app replaces it. Six digits there are the emailed
    /// code's, always: the secret is never decrypted and no authenticator code checked.
    /// </summary>
    [Fact]
    public async Task AnAuthenticatorSecretOnAnEmailedCodeRow_IsNeverChecked()
    {
        GivenRow(EmailRow(_user.Id, protectedSecretKey: "v2:pending-app-secret"));
        GivenRecoveryChallenge("111111");
        _totp.Setup(t => t.ValidateCode(It.IsAny<string>(), "222222")).Returns(59_313_872);

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, "222222", null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode, "the code is checked as the emailed code, and it is not that");
        _totp.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never,
            "the authenticator's counter is never reserved for it");
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThePasswordEntry_WithAWrongEmailedCode_RecoversNothing()
    {
        GivenRow(EmailRow(_user.Id));
        GivenRecoveryChallenge("111111");

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, "999999", null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
        _requests.Verify(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TheProviderEntry_RefusesSixDigits_OnAnEmailFactorAccount_Unchecked()
    {
        GivenRow(EmailRow(_user.Id));
        GivenRecoveryChallenge("111111");

        var result = await Recoverer().RecoverAsync(
            _user, Request(), AuthenticationMethods.ExternalIdentity, "111111", null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode,
            "the provider proved the mailbox the code would go to, so a code sent there proves nothing new");
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1)] // the password entry
    [InlineData(2)] // the provider entry
    public async Task ARecoveryCode_RecoversEitherEntry_OnEitherFactor_AndIsSpent(int primary)
    {
        GivenRow(TotpRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]"));
        _store.Setup(s => s.TrySpendRecoveryCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);
        var first = AuthenticationMethods.FromStored(primary);

        var result = await Recoverer().RecoverAsync(_user, Request(), first, RecoveryCode, null, null, null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _signedInWith.Should().Be(first.With(AuthenticationMethods.RecoveryCode));
        _store.Verify(s => s.TrySpendRecoveryCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    [InlineData("ABCD-EFG1")] // a 1 is never in a recovery code
    public async Task AMistypedCode_IsTheFactorsOwnWrongCode_AndCounted_AsBeforeRecoveryCodesWereAccepted(string code)
    {
        // An authenticator account whose recovery codes are all spent: a recovery-code
        // answer here would say "no recovery codes are available", a dead end on a typo.
        GivenRow(TotpRow(_user.Id, recoveryCodes: "[]"));

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, code, null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
        _store.Verify(s => s.TryReserveAttemptAsync(_user.Id, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once,
            "a wrong code is a counted failure, whatever it looks like");
        _totp.Verify(t => t.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _store.Verify(s => s.TrySpendRecoveryCodeAsync(It.IsAny<Guid>(), It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("ABCD-EFGH")]
    [InlineData("abcd efgh")]
    [InlineData("ABCDEFGH")]
    public async Task ARecoveryCodesShape_TakesTheRecoveryCodeBranch(string code)
    {
        GivenRow(TotpRow(_user.Id, recoveryCodes: "[]"));

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, code, null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.NoRecoveryCodesAvailable);
        _totp.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ARecoveryCodeSpentByAnotherRequestFirst_RecoversNothing()
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]"));
        _store.Setup(s => s.TrySpendRecoveryCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.FactorLost);

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, RecoveryCode, null, null, null, CancellationToken.None);

        result.FirstError.Should().Be(TwoFactorErrors.InvalidRecoveryCode);
    }

    [Theory]
    [InlineData(AccountDeletionSource.InApp, "[\"h1\"]", 100)]  // recovery codes remain: under the limit (ARR-0693)
    [InlineData(AccountDeletionSource.InApp, null, null)]       // none remain, and the account's own session asked: timed lock only
    [InlineData(AccountDeletionSource.PublicWeb, null, 100)]    // asked through the public page: under the limit
    public async Task TheEmailedCode_IsUnderTheLimit_UnlessNothingElseWouldRecoverTheAccount(
        AccountDeletionSource source, string? recoveryCodes, int? expectedLimit)
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: recoveryCodes));
        GivenRecoveryChallenge("111111");

        await Recoverer().RecoverAsync(_user, Request(source), AuthenticationMethods.Password, "111111", null, null, null, CancellationToken.None);

        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_user.Id, expectedLimit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AskingForAnEmailedCode_OpensTheRecoverysOwnChallenge_AndSendsUnderTheLimit()
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: "[\"h1\"]"));
        TwoFactorChallenge? opened = null;
        _challenges.Setup(c => c.CreateAsync(It.IsAny<TwoFactorChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<TwoFactorChallenge, CancellationToken>((challenge, _) => opened = challenge);
        _sender.Setup(s => s.SendAsync(_user, It.IsAny<TwoFactorChallenge>(), "203.0.113.7", 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailCodeSent(DateTime.UtcNow, "m", 2));

        var sent = await Recoverer().SendEmailCodeAsync(_user, Request(AccountDeletionSource.InApp), "203.0.113.7", CancellationToken.None);

        sent.IsError.Should().BeFalse();
        opened!.Purpose.Should().Be(TwoFactorChallengePurpose.DeletionRecovery);
        opened.PrimaryMethod.Should().Be(AuthenticationMethods.Password);
    }

    [Fact]
    public async Task AskingForAnEmailedCode_OnAnyOtherAccount_SendsNothing_AndAnswersLikeAWrongPassword()
    {
        GivenRow(TotpRow(_user.Id));

        var sent = await Recoverer().SendEmailCodeAsync(_user, Request(), null, CancellationToken.None);

        sent.FirstError.Should().Be(UserErrors.InvalidCredentials,
            "a different answer would confirm the password without recovering anything, and without the owner hearing of it");
        _sender.VerifyNoOtherCalls();

        var noFactor = TestHelpers.CreateUser(id: _user.Id, twoFactorEnabled: false, isDeleted: true);
        (await Recoverer().SendEmailCodeAsync(noFactor, Request(), null, CancellationToken.None))
            .FirstError.Should().Be(UserErrors.InvalidCredentials);
    }

    [Fact]
    public async Task AnUnqualifiedRecoveryCode_OnAnEmailFactorAccount_RecoversWithTheFirstFactorAlone()
    {
        GivenRow(EmailRow(_user.Id, recoveryCodes: "[\"h1\",\"h2\"]", recoveryCodesQualified: false));
        _store.Setup(s => s.TrySpendRecoveryCodeAsync(_user.Id, It.IsAny<SecondFactorProof>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginCommitOutcome.Committed);

        var result = await Recoverer().RecoverAsync(_user, Request(), AuthenticationMethods.Password, RecoveryCode, null, null, null, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _signedInWith.Should().Be(AuthenticationMethods.Password,
            "codes issued when the emailed code was switched on, after the password alone, prove no second factor");
    }
}

/// <summary>
/// The recovery requests' code field (ARR-1083): a recovery code as shown passes, nothing
/// longer than any code of the account does.
/// </summary>
public class RecoverRequestContractTests
{
    [Theory]
    [InlineData("ABCD-EFGH")]
    [InlineData("123456")]
    [InlineData("ABCDEFGHJKLMNPQR")] // 16
    [InlineData(null)]
    public void BothEntries_AcceptEveryCodeOfTheAccount(string? code)
    {
        new RecoverAccountCommandValidator()
            .Validate(new RecoverAccountCommand("owner@example.com", "password", code, null, null)).IsValid.Should().BeTrue();
        new RecoverAccountExternalCommandValidator()
            .Validate(new RecoverAccountExternalCommand("google", "id-token", null, code, null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void BothEntries_RefuseALongerField_WithTheirPublishedCode()
    {
        const string code = "ABCDEFGHJKLMNPQRS"; // 17

        new RecoverAccountCommandValidator()
            .Validate(new RecoverAccountCommand("owner@example.com", "password", code, null, null))
            .Errors.Should().ContainSingle(e => e.ErrorCode == TwoFactorErrors.CodeTooLong.Code && e.PropertyName == "TwoFactorCode");
        new RecoverAccountExternalCommandValidator()
            .Validate(new RecoverAccountExternalCommand("google", "id-token", null, code, null, null))
            .Errors.Should().ContainSingle(e => e.ErrorCode == TwoFactorErrors.CodeTooLong.Code && e.PropertyName == "TwoFactorCode");
    }

    [Fact]
    public void AskingForAnEmailedCode_ChecksTheCredentialsAsTheRecoveryDoes()
    {
        var validator = new SendDeletionRecoveryEmailCodeCommandValidator();

        validator.Validate(new SendDeletionRecoveryEmailCodeCommand("owner@example.com", "password", null, null)).IsValid.Should().BeTrue();
        validator.Validate(new SendDeletionRecoveryEmailCodeCommand("not-an-email", "", null, null)).IsValid.Should().BeFalse();
    }
}
