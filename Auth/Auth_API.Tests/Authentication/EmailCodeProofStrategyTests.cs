using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;
using static Auth_API.Tests.Helpers.EmailFactorFixtures;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The emailed code as a second factor (S24, EO5, FA4 (4-3)): checked against the one
/// challenge it was emailed for, on a counter of its own, with the codes a newer send
/// replaced told apart before anything is counted.
/// </summary>
public class EmailCodeProofStrategyTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<IPublisher> _publisher = new();

    private SecondFactorVerifier Verifier() => new(
        _store.Object,
        [
            new TotpProofStrategy(_totp.Object, Mock.Of<ITwoFactorSecretProtector>()),
            new RecoveryCodeProofStrategy(_totp.Object),
            new EmailCodeProofStrategy(ScopedOtpHasher.Instance)
        ],
        TestHelpers.CreateSecondFactorLockout(publisher: _publisher.Object));

    private void GivenRow(TwoFactorSnapshot row, int? counted = 1)
    {
        _store.Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(counted);
    }

    private async Task<ErrorOr.ErrorOr<SecondFactorProof>> PresentAsync(TwoFactorChallenge challenge, string code)
    {
        var verifier = Verifier();
        var reservation = await verifier.ReserveAsync(
            new SecondFactorPresentation(_userId, SecondFactorMethod.EmailCode, code, challenge), CancellationToken.None);

        return reservation.IsError
            ? reservation.Errors
            : await verifier.VerifyAsync(reservation.Value, code, SecondFactorMethod.EmailCode, CancellationToken.None);
    }

    [Fact]
    public async Task TheLiveCode_ProvesTheEmailedCode_RecordedAsSuch_AndNeverAsASecondFactor()
    {
        GivenRow(EmailRow(_userId));
        var challenge = ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111", "222222"]);

        var proof = await PresentAsync(challenge, "222222");

        proof.IsError.Should().BeFalse();
        proof.Value.Method.Should().Be(SecondFactorMethod.EmailCode);
        proof.Value.ChallengeId.Should().Be(challenge.Id);
        proof.Value.EmailCodesSeen.Should().Be(challenge.EmailCodeHash, "the commit spends the codes exactly as checked");
        AuthenticationMethods.Password.With(proof.Value.Methods).IsMfaSatisfied.Should().BeFalse(
            "whoever holds the mailbox holds the password reset: the emailed code satisfies no policy");
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_userId, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AWrongCode_IsRefused_AfterItsAttemptWasCountedOnTheEmailCounterAlone()
    {
        GivenRow(EmailRow(_userId));

        var proof = await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]), "999999");

        proof.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never,
            "a wrong emailed code never locks the recovery codes the person is left with");
    }

    [Fact]
    public async Task ACodeEmailedForAnotherChallenge_IsWrongHere()
    {
        GivenRow(EmailRow(_userId));
        var other = ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["333333"]);
        var challenge = ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]);
        // The other challenge's stored form, moved onto this one: the hash names its challenge.
        var moved = new TwoFactorChallenge(
            challenge.Id, _userId, "token-hash", null, challenge.ExpiresAt, null, 0, challenge.CreatedAt,
            AuthenticationMethods.Password, TwoFactorChallengePurpose.SignIn, other.EmailCodeHash, DateTime.UtcNow, 1);

        var proof = await PresentAsync(moved, "333333");

        proof.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
    }

    [Fact]
    public async Task NoCodeSentYet_NothingMatches()
    {
        GivenRow(EmailRow(_userId));

        var proof = await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password), "111111");

        proof.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
    }

    [Fact]
    public async Task AnEarlierCode_IsSuperseded_BeforeAnythingIsCounted()
    {
        GivenRow(EmailRow(_userId));

        var proof = await PresentAsync(
            ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111", "222222", "333333"]), "111111");

        proof.FirstError.Should().Be(TwoFactorErrors.EmailCodeSuperseded);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never,
            "only the holder of the mailbox could present an earlier code: it is no guess, and counting it would lock the owner out");
    }

    [Theory]
    [InlineData(4)] // EmailCode (a verify-email link)
    [InlineData(0)] // Unknown (a challenge recorded before first factors were)
    public async Task AfterAFirstFactorOtherThanThePasswordOrAProvider_TheEmailedCodeIsUnavailable_Uncounted(int primary)
    {
        GivenRow(EmailRow(_userId));

        var proof = await PresentAsync(
            ChallengeWith(_userId, AuthenticationMethods.FromStored(primary), codes: ["111111"]), "111111");

        proof.FirstError.Should().Be(TwoFactorErrors.EmailCodeUnavailable);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAuthenticatorCode_OnARowWhoseFactorIsTheEmailedCode_IsWrong_Uncounted_AndNeverThrows()
    {
        GivenRow(EmailRow(_userId));
        var verifier = Verifier();

        var reservation = await verifier.ReserveAsync(
            new SecondFactorPresentation(_userId, SecondFactorMethod.Totp, "123456", null), CancellationToken.None);

        reservation.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
        _store.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheTotpStrategy_GivenARowWithNoSecret_RefusesTheCode_InsteadOfThrowing()
    {
        var reservation = new SecondFactorReservation(EmailRow(_userId));

        var proof = await new TotpProofStrategy(_totp.Object, Mock.Of<ITwoFactorSecretProtector>())
            .VerifyAsync(reservation, "123456", CancellationToken.None);

        proof.FirstError.Should().Be(UserErrors.InvalidTwoFactorCode);
    }

    [Fact]
    public async Task AnEmailCodeAtTheLimit_IsLockedForGood_AndNamedSo_Uncounted()
    {
        GivenRow(EmailRow(_userId, emailFailedAttempts: 100));

        var proof = await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]), "111111");

        proof.FirstError.Should().Be(TwoFactorErrors.EmailCodeLocked);
        _store.Verify(s => s.TryReserveEmailCodeAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnEmailCodeLockedForAWhile_IsLockedOut_Uncounted()
    {
        GivenRow(EmailRow(_userId, emailFailedAttempts: 5, emailLockedUntil: DateTime.UtcNow.AddMinutes(10)));

        var proof = await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]), "111111");

        proof.FirstError.Should().Be(TwoFactorErrors.LockedOut);
    }

    [Fact]
    public async Task TheReservationThatReachesTheLimit_IsReportedAsTheSecondFactorsHardLock()
    {
        GivenRow(EmailRow(_userId, emailFailedAttempts: 99), counted: 100);

        await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]), "999999");

        _publisher.Verify(p => p.Publish(
            It.Is<UserHardLockedEvent>(e => e.UserId == _userId && e.Factor == HardLockedFactor.SecondFactor && e.FailedAttempts == 100),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AConcurrentRequestThatLockedTheEmailCodeFirst_IsAnsweredByTheLockItHit()
    {
        _store.SetupSequence(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailRow(_userId, emailFailedAttempts: 99))
            .ReturnsAsync(EmailRow(_userId, emailFailedAttempts: 100));
        _store.Setup(s => s.TryReserveEmailCodeAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        var proof = await PresentAsync(ChallengeWith(_userId, AuthenticationMethods.Password, codes: ["111111"]), "111111");

        proof.FirstError.Should().Be(TwoFactorErrors.EmailCodeLocked);
    }

    [Fact]
    public void TheCodesScope_NamesItsChallenge()
    {
        var challengeId = Guid.NewGuid();

        EmailCodeProofStrategy.HashScope(challengeId).Should().Be("two-factor-email:" + challengeId);
        EmailCodeProofStrategy.HashScope(Guid.NewGuid()).Should().NotBe(EmailCodeProofStrategy.HashScope(challengeId));
    }
}
