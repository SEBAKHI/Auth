using Auth.Application.DTOs;
using Auth.Application.Features.AccountDeletion.Common;
using Auth.Application.Features.AccountDeletion.RecoverAccountWithPasskey;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
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

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 PR B, D-S20-6 (a): a passkey cancels its account's pending deletion. The passkey sign-in's
/// own steps (shared, never copied), then an account waiting for deletion and nothing else, the
/// lock as at the sign-in, then the shared recovery: the request cancelled, the account restored,
/// the cancellation published, and a session with the passkey's methods that asks no second step
/// (M3) and lifts no lock. An administrator whose only usable factor is a passkey recovers this
/// way, while the three other entries refuse that account (D-S20-4 (ii-s)).
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: the recoverer's second-step gate asked of a passkey;
/// <c>passwordProven</c> true for the recovered session; the refusal asked of the passkey entry
/// too.
/// </remarks>
public class RecoverAccountWithPasskeyCommandHandlerTests
{
    private static readonly AuthenticationMethods PasskeyMethods =
        AuthenticationMethods.Passkey.With(AuthenticationMethods.PasskeyUserVerified);

    private static readonly QualifyingSecondFactors PasskeyOnly = new(Totp: false, Passkey: true);

    private readonly Mock<IPublisher> _publisher = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_AccountWaitingForDeletion_IsRestoredAndSignedInWithNoSecondStep(bool authenticatorAppOn)
    {
        using var fixture = Fixture(authenticatorAppOn);
        var deletion = WaitingFor(fixture);

        var result = await Recover(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOff());

        result.IsError.Should().BeFalse(result.IsError ? result.FirstError.Code : "");
        deletion.Status.Should().Be(AccountDeletionStatus.Cancelled);
        fixture.Deletions.Verify(r => r.UpdateAsync(deletion, AccountDeletionStatus.PendingGrace, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Users.Verify(u => u.RestoreAsync(fixture.Owner.Id, It.IsAny<CancellationToken>()), Times.Once);
        _publisher.Verify(p => p.Publish(It.Is<AccountDeletionCancelledEvent>(e => e.UserId == fixture.Owner.Id), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Builder.Verify(b => b.BuildAsync(
                It.Is<User>(user => user.Id == fixture.Owner.Id), "203.0.113.7", "Agent/1.0", "device-1", PasskeyMethods,
                false, It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Once, "the passkey proved two factors and never the password");
    }

    [Fact]
    public async Task Handle_PasskeyOnlyAdministrator_RecoversWithThePasskey()
    {
        using var fixture = Fixture();
        WaitingFor(fixture);

        var result = await Recover(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOn(PasskeyOnly, holdsPlatformAuthority: true));

        result.IsError.Should().BeFalse("D-S20-6 (a): without this entry such an account would be deleted at the grace period's end");
        fixture.Users.Verify(u => u.RestoreAsync(fixture.Owner.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<string, AuthenticationMethods> TheOtherEntries => new()
    {
        { "the password entry", AuthenticationMethods.Password },
        { "the provider entry", AuthenticationMethods.ExternalIdentity },
        { "the emailed-code entry", AuthenticationMethods.ForEmailCodeSignIn(accountHasPassword: false) },
    };

    [Theory]
    [MemberData(nameof(TheOtherEntries))]
    public async Task RecoverAsync_OtherEntryForPasskeyOnlyAdministrator_RefusesAndChangesNothing(string entry, AuthenticationMethods primary)
    {
        using var fixture = Fixture();
        var deletion = WaitingFor(fixture);
        var recoverer = Recoverer(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOn(PasskeyOnly, holdsPlatformAuthority: true));
        var spent = false;

        var result = await recoverer.RecoverAsync(
            fixture.DeletedOwner!, deletion, primary, twoFactorCode: null, "203.0.113.7", "Agent/1.0", "device-1",
            CancellationToken.None, spendFirstProof: _ => { spent = true; return Task.FromResult(true); });

        result.FirstError.Code.Should().Be(AuthErrors.PasskeyRequired.Code, entry);
        result.FirstError.Type.Should().Be(ErrorType.Forbidden, entry);
        deletion.Status.Should().Be(AccountDeletionStatus.PendingGrace, entry);
        spent.Should().BeFalse($"{entry}: the emailed code is not spent on a refusal");
        fixture.Deletions.Verify(r => r.UpdateAsync(It.IsAny<AccountDeletionRequest>(), It.IsAny<AccountDeletionStatus>(), It.IsAny<CancellationToken>()), Times.Never, entry);
        fixture.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never, entry);
        fixture.VerifyNoSession();
    }

    [Theory]
    [MemberData(nameof(TheOtherEntries))]
    public async Task RecoverAsync_OtherEntryForAnyOtherAccount_RecoversAsBefore(string entry, AuthenticationMethods primary)
    {
        // An administrator who also has the app, and a passkey-only account without platform permission.
        foreach (var (factors, administrator) in new[] { (new QualifyingSecondFactors(Totp: true, Passkey: true), true), (PasskeyOnly, false) })
        {
            using var fixture = Fixture();
            var deletion = WaitingFor(fixture);

            var result = await Recoverer(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOn(factors, administrator)).RecoverAsync(
                fixture.DeletedOwner!, deletion, primary, twoFactorCode: null, "203.0.113.7", "Agent/1.0", "device-1", CancellationToken.None);

            result.IsError.Should().BeFalse($"{entry}, app {factors.Totp}, administrator {administrator}: {(result.IsError ? result.FirstError.Code : "")}");
            deletion.Status.Should().Be(AccountDeletionStatus.Cancelled);
        }
    }

    [Theory]
    [InlineData("a live account")]
    [InlineData("deleted by an administrator, with no request")]
    [InlineData("its request claimed after the grace period")]
    public async Task Handle_AccountNotWaitingForDeletion_ReturnsInvalidCredentials(string state)
    {
        using var fixture = Fixture();
        switch (state)
        {
            case "a live account":
                fixture.DeletedOwner = fixture.Owner;
                break;
            case "deleted by an administrator, with no request":
                fixture.Deletions.Setup(r => r.GetActiveByUserIdAsync(fixture.Owner.Id, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((AccountDeletionRequest?)null);
                break;
            default:
                var claimed = AccountDeletionRequest.Create(fixture.Owner.Id, AccountDeletionSource.InApp, TimeSpan.Zero, "2026.07", fixture.Owner.Id);
                claimed.Claim().IsError.Should().BeFalse();
                fixture.Deletions.Setup(r => r.GetActiveByUserIdAsync(fixture.Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(claimed);
                break;
        }

        var result = await Recover(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOff());

        result.FirstError.Code.Should().Be(UserErrors.InvalidCredentials.Code, state);
        fixture.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never, state);
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task Handle_AdministratorsLock_RefusesWithThePasswordEntrysAnswer()
    {
        using var fixture = Fixture();
        fixture.DeletedOwner = Deleted(fixture, UserStatus.Locked, failures: 0);
        var deletion = WaitingFor(fixture);

        var result = await Recover(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOff());

        result.FirstError.Code.Should().Be(fixture.DeletedOwner.LockRefusal(AccountLockKind.Admin).Code);
        deletion.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        fixture.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNoSession();
    }

    [Fact]
    public async Task Handle_HardLockRaisedByWrongPasswords_AdmitsAndLiftsNothing()
    {
        using var fixture = Fixture();
        var locked = Deleted(fixture, UserStatus.Locked, failures: 100);
        fixture.DeletedOwner = locked;
        WaitingFor(fixture);

        var result = await Recover(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOff());

        result.IsError.Should().BeFalse("the hard lock admits a passkey (ARR-1043)");
        locked.Status.Should().Be(UserStatus.Locked);
        locked.FailedLoginAttempts.Should().Be(100);
        fixture.Builder.Verify(b => b.BuildAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), PasskeyMethods,
                false, It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<Guid?>(),
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<bool>()),
            Times.Once, "admitted, and no lock lifted: the session does not prove the password (G8)");
    }

    [Fact]
    public async Task Handle_AssertionFails_SpendsTheChallengeAndRecoversNothing()
    {
        using var fixture = Fixture();
        var deletion = WaitingFor(fixture);
        var response = await fixture.AnswerAsync(new CeremonyTweaks { CorruptSignature = true });

        var result = await fixture.Recover(Recoverer(fixture, TestHelpers.PasskeyOnlyAdministratorRuleOff()))
            .Handle(fixture.RecoverCommand(response), CancellationToken.None);

        result.FirstError.Code.Should().Be(PasskeyErrors.VerificationFailed.Code);
        fixture.ConsumeCalls.Should().Be(1, "the challenge is spent before any check, as at the sign-in");
        deletion.Status.Should().Be(AccountDeletionStatus.PendingGrace);
        fixture.Users.Verify(u => u.RestoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNoSession();
    }

    [Fact]
    public void ToString_NeverPrintsTheHandleOrTheResponse()
    {
        new RecoverAccountWithPasskeyCommand("secret-handle", false, "{\"secret\":1}", "203.0.113.7", "Agent", "d")
            .ToString().Should().NotContain("secret");
    }

    // ── Fixture ──

    /// <summary>
    /// The passkey's owner as the recovery reads it: soft-deleted. The read of live accounts
    /// (the recoverer's re-read after the restore) finds it live again.
    /// </summary>
    private static PasskeySignInFixture Fixture(bool authenticatorAppOn = false)
    {
        var fixture = new PasskeySignInFixture();
        fixture.DeletedOwner = Deleted(fixture, twoFactorEnabled: authenticatorAppOn);
        return fixture;
    }

    private static User Deleted(
        PasskeySignInFixture fixture, UserStatus status = UserStatus.Active, int failures = 0, bool twoFactorEnabled = false) =>
        TestHelpers.CreateUser(
            id: fixture.Owner.Id, email: "owner@example.com", status: status, failedLoginAttempts: failures,
            twoFactorEnabled: twoFactorEnabled, isDeleted: true, deletedAt: DateTime.UtcNow);

    private static AccountDeletionRequest WaitingFor(PasskeySignInFixture fixture)
    {
        var deletion = AccountDeletionRequest.Create(fixture.Owner.Id, AccountDeletionSource.InApp, TimeSpan.FromDays(30), "2026.07", fixture.Owner.Id);
        fixture.Deletions.Setup(r => r.GetActiveByUserIdAsync(fixture.Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(deletion);
        fixture.Deletions.Setup(r => r.UpdateAsync(deletion, AccountDeletionStatus.PendingGrace, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return deletion;
    }

    private AccountDeletionRecoverer Recoverer(PasskeySignInFixture fixture, PasskeyOnlyAdministratorRule rule) => new(
        fixture.Deletions.Object,
        fixture.Users.Object,
        // Strict: a passkey proved two factors, and no other entry here has a second factor on.
        new Mock<ISecondFactorVerifier>(MockBehavior.Strict).Object,
        new Mock<ITwoFactorStateStore>(MockBehavior.Strict).Object,
        new Mock<ITwoFactorChallengeRepository>(MockBehavior.Strict).Object,
        new Mock<IEmailCodeSender>(MockBehavior.Strict).Object,
        new FakeCredentialHasher(),
        new Mock<IJwtTokenService>(MockBehavior.Strict).Object,
        TestHelpers.CreateSecondFactorLockout(),
        fixture.Builder.Object,
        rule,
        _publisher.Object,
        NullLogger<AccountDeletionRecoverer>.Instance);

    private async Task<ErrorOr<LoginResponse>> Recover(PasskeySignInFixture fixture, PasskeyOnlyAdministratorRule rule) =>
        await fixture.Recover(Recoverer(fixture, rule)).Handle(fixture.RecoverCommand(await fixture.AnswerAsync()), CancellationToken.None);
}
