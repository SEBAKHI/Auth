using Auth.Application.Configuration;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using MediatR;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12 PR B, LK1.2 and LK3: at Hard′ consecutive wrong codes the second factor
/// accepts no code — from the authenticator app or a recovery code, on every path,
/// a pending factor included — and answers <c>TwoFactor.LockedAdminResetRequired</c>,
/// never the timed <c>TwoFactor.LockedOut</c>. Below the limit nothing changes.
/// </summary>
/// <remarks>
/// <para>
/// The verifier is the one place every caller reserves through (eight of them), so
/// the cap is proved here once: the limit it hands the store, the answer from the
/// row as read, and the answer for a reservation the store refused. The statement's
/// own condition is pinned by <c>TwoFactorStateStoreSqlTests</c>, and the recoverer's
/// visible answer by <c>RecoverAccountCommandHandlerTests</c>.
/// </para>
/// <para>
/// Deliberate breaks: drop the hard-lock fast path (the snapshot cases turn red);
/// classify every refused reservation as <c>LockedOut</c> again (the burst case turns
/// red); pass no limit to the store (the limit case turns red).
/// </para>
/// </remarks>
public class TwoFactorHardLockTests
{
    private const int Limit = 100;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ISecondFactorProofStrategy> _totp = new();
    private readonly Mock<ISecondFactorProofStrategy> _recoveryCode = new();

    public TwoFactorHardLockTests()
    {
        _totp.SetupGet(s => s.Method).Returns(SecondFactorMethod.Totp);
        _recoveryCode.SetupGet(s => s.Method).Returns(SecondFactorMethod.RecoveryCode);
    }

    private SecondFactorVerifier Verifier(ConsecutiveFailureMode mode = ConsecutiveFailureMode.Enforce)
    {
        var settings = TestHelpers.CreatePasswordSettings();
        settings.ConsecutiveFailureMode = mode;
        return new SecondFactorVerifier(
            _store.Object,
            [_totp.Object, _recoveryCode.Object],
            TestHelpers.CreateSecondFactorLockout(settings, _publisher.Object));
    }

    private TwoFactorSnapshot Snapshot(int failedAttempts, bool isEnabled = true, DateTime? lockedUntil = null) =>
        new(_userId, "v2:secret", "[\"h1\"]", isEnabled, failedAttempts, lockedUntil, modifiedAt: DateTime.UtcNow.AddMinutes(-1));

    private void GivenRead(params TwoFactorSnapshot?[] reads)
    {
        var sequence = _store.SetupSequence(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()));
        foreach (var read in reads)
        {
            sequence = sequence.ReturnsAsync(read);
        }
    }

    private void GivenReservation(int? count) =>
        _store
            .Setup(s => s.TryReserveAttemptAsync(_userId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(count);

    private void VerifyNothingReserved() =>
        _store.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a factor at the limit counts nothing more");

    [Theory]
    [InlineData(Limit)]
    [InlineData(Limit + 7)] // a limit lowered below counts already there
    public async Task AtOrPastTheLimit_EnabledFactor_AnswersLockedAdminResetRequired_WithoutAReservation(int failedAttempts)
    {
        GivenRead(Snapshot(failedAttempts));

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
        result.FirstError.Code.Should().Be("TwoFactor.LockedAdminResetRequired");
        result.FirstError.Type.Should().Be(ErrorOr.ErrorType.Forbidden, "403, like every lock");
        VerifyNothingReserved();
    }

    [Fact]
    public async Task AtTheLimit_WhileAlsoTimedLocked_IsNeverAnsweredLockedOut()
    {
        // The reservation that reached the limit also set the timed lock (every
        // failure past five does). Waiting it out changes nothing, so the answer
        // must name the way out, not a wait.
        GivenRead(Snapshot(Limit, lockedUntil: DateTime.UtcNow.AddMinutes(15)));

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
    }

    [Fact]
    public async Task AtTheLimit_NoCodeOfEitherKindIsChecked()
    {
        // The reservation is refused before any code: the authenticator app's and
        // a recovery code alike — they share the counter.
        GivenRead(Snapshot(Limit));

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeTrue("there is no reservation to verify a code against");
        _totp.Verify(s => s.VerifyAsync(It.IsAny<TwoFactorSnapshot>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _recoveryCode.Verify(s => s.VerifyAsync(It.IsAny<TwoFactorSnapshot>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtTheLimit_PendingFactor_IsCappedToo()
    {
        // Enable and the recoverer's disagreeing-flag path verify a pending row; the
        // limit is uniform (ARR-0502).
        GivenRead(Snapshot(Limit, isEnabled: false));

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: false, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
        VerifyNothingReserved();
    }

    [Fact]
    public async Task BelowTheLimit_ReservesWithTheLimit_AsToday()
    {
        GivenRead(Snapshot(Limit - 1));
        GivenReservation(Limit - 1 + 1);

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeFalse("one below the limit still gets its attempt");
        _store.Verify(s => s.TryReserveAttemptAsync(_userId, Limit, It.IsAny<CancellationToken>()), Times.Once,
            "the store applies the same limit in the statement that counts");
    }

    [Fact]
    public async Task TheReservationThatReachesTheLimit_PublishesTheSecondFactorHardLock_Once()
    {
        GivenRead(Snapshot(Limit - 1));
        GivenReservation(Limit);

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeFalse("the attempt that reaches the limit still has its code checked");
        _publisher.Verify(p => p.Publish(
            It.Is<UserHardLockedEvent>(e => e.UserId == _userId && e.Factor == HardLockedFactor.SecondFactor && e.FailedAttempts == Limit),
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task AnOrdinaryReservation_PublishesNothing()
    {
        GivenRead(Snapshot(3));
        GivenReservation(4);

        await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        _publisher.Verify(p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefusedReservation_ThatABurstDroveToTheLimit_AnswersLockedAdminResetRequired()
    {
        // The read saw 99; a concurrent request took the last attempt; the row read
        // again after the refusal is at the limit.
        GivenRead(Snapshot(Limit - 1), Snapshot(Limit));
        GivenReservation(null);

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedAdminResetRequired.Code);
    }

    [Fact]
    public async Task RefusedReservation_BelowTheLimit_StaysTheTimedLock()
    {
        GivenRead(Snapshot(4), Snapshot(5, lockedUntil: DateTime.UtcNow.AddMinutes(15)));
        GivenReservation(null);

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
    }

    [Fact]
    public async Task RefusedReservation_OnARowThatIsGone_IsTheAnswerItAlwaysWas()
    {
        GivenRead(Snapshot(4), null);
        GivenReservation(null);

        var result = await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
    }

    [Fact]
    public void Snapshot_IsHardLocked_OnlyAtTheLimitAndOnlyWhenOneIsInForce()
    {
        Snapshot(Limit).IsHardLocked(Limit).Should().BeTrue();
        Snapshot(Limit - 1).IsHardLocked(Limit).Should().BeFalse();
        Snapshot(Limit + 50).IsHardLocked(null).Should().BeFalse("Legacy caps nothing");
    }
}
