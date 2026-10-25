using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth_API.Tests.Helpers;
using Auth_API.Tests.Infrastructure;
using MediatR;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// X12 PR B, owner decisions 2026-10-12 on the row «pending deletion»: (b), narrowed
/// by D-66-1 (a). The recovery of a deletion that the account's own signed-in
/// session asked for keeps the timed lock alone. No way out of the hard lock reaches
/// an account waiting for deletion, so the consecutive-failure limit would cost that
/// owner the account; the timed rhythm (about 96 guesses a day) is the residual the
/// owner accepted. A deletion asked through the public page recovers under the cap
/// (RecoverAccountCommandHandlerTests).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISecondFactorVerifier.ReserveUnderTheTimedLockOnlyAsync"/> is the
/// verifier's one reservation with no limit: it hands the store no limit, never
/// answers <c>TwoFactor.LockedAdminResetRequired</c>, and reaching the limit on it
/// is no transition. The recoverer is the only production file that names it — a
/// call, a method group or a delegate alike — read from the source, so every other
/// path stays capped (stop condition 4 of the notes).
/// </para>
/// <para>
/// Deliberate breaks: pass the limit on this path (the store and answer cases turn
/// red); report the reservation on it (the transition case turns red); name it in
/// another production file, even as a method group (the guard turns red).
/// </para>
/// </remarks>
public class TimedLockOnlyReservationTests
{
    private const int Limit = 100;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITwoFactorStateStore> _store = new();
    private readonly Mock<IPublisher> _publisher = new();
    private readonly Mock<ISecondFactorProofStrategy> _totp = new();

    public TimedLockOnlyReservationTests()
    {
        _totp.SetupGet(s => s.Method).Returns(SecondFactorMethod.Totp);
    }

    private SecondFactorVerifier Verifier() =>
        new(_store.Object, [_totp.Object], TestHelpers.CreateSecondFactorLockout(publisher: _publisher.Object));

    private TwoFactorSnapshot Snapshot(int failedAttempts, DateTime? lockedUntil = null) =>
        new(_userId, "v2:secret", "[\"h1\"]", true, failedAttempts, lockedUntil, modifiedAt: DateTime.UtcNow.AddMinutes(-1));

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

    [Theory]
    [InlineData(Limit)]
    [InlineData(Limit + 7)]
    public async Task AtOrPastTheLimit_AnAttemptIsStillReserved_WithNoLimit(int failedAttempts)
    {
        GivenRead(Snapshot(failedAttempts));
        GivenReservation(failedAttempts + 1);

        var result = await Verifier().ReserveUnderTheTimedLockOnlyAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeFalse("the code is checked under the timed lock alone");
        _store.Verify(
            s => s.TryReserveAttemptAsync(_userId, (int?)null, It.IsAny<CancellationToken>()),
            Times.Once,
            "the store is handed no limit on this path");
        _store.Verify(
            s => s.TryReserveAttemptAsync(_userId, It.Is<int?>(limit => limit != null), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReachingTheLimit_OnThisPath_IsNoTransition()
    {
        GivenRead(Snapshot(Limit - 1));
        GivenReservation(Limit);

        var result = await Verifier().ReserveUnderTheTimedLockOnlyAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _publisher.Verify(
            p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the factor is not hard-locked for this path, so nothing is reported");
    }

    [Fact]
    public async Task TheSameCount_OnTheCappedPath_IsTheTransition()
    {
        // The other side of the case above, so it cannot pass for a reason of its own.
        GivenRead(Snapshot(Limit - 1));
        GivenReservation(Limit);

        await Verifier().ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        _publisher.Verify(
            p => p.Publish(It.IsAny<UserHardLockedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ATimedLock_StillRefuses_AsLockedOut_EvenPastTheLimit()
    {
        GivenRead(Snapshot(Limit + 3, lockedUntil: DateTime.UtcNow.AddMinutes(15)));

        var result = await Verifier().ReserveUnderTheTimedLockOnlyAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
        _store.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ARefusedReservation_PastTheLimit_IsLockedOut_NeverLockedAdminResetRequired()
    {
        // A burst locked the factor for a while between the read and the reservation.
        GivenRead(Snapshot(Limit), Snapshot(Limit + 5, lockedUntil: DateTime.UtcNow.AddMinutes(15)));
        GivenReservation(null);

        var result = await Verifier().ReserveUnderTheTimedLockOnlyAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be(TwoFactorErrors.LockedOut.Code);
    }

    [Fact]
    public void OnlyTheRecoveryOfAnAccountWaitingForDeletion_ReservesWithoutTheLimit()
    {
        // Every occurrence of the member's name, not only a literal call: a method
        // group or a delegate would reach the exempt path just the same. The
        // interface declares it and the verifier implements it.
        const string member = nameof(ISecondFactorVerifier.ReserveUnderTheTimedLockOnlyAsync);
        string[] declaring = ["ISecondFactorVerifier.cs", "SecondFactorVerifier.cs"];

        var uses = ApiSourceScan.ProductionSources()
            .Select(file => (Name: Path.GetFileName(file.File), Count: file.Source.Split(member).Length - 1))
            .Where(file => file.Count > 0 && !declaring.Contains(file.Name))
            .ToList();

        // Three uses in the recoverer, one per kind of code it accepts (S24): the
        // authenticator app's and a recovery code under X12's exception (a deletion
        // the account's own session asked for), and the emailed code under the
        // narrower one (that, AND no recovery code left: ARR-0693).
        uses.Should().Equal([("AccountDeletionRecoverer.cs", 3)],
            "every other path verifies a factor whose hard lock has a way out, so it stays capped");
    }
}
