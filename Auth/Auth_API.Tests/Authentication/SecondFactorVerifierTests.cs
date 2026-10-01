using System.Text.Json;
using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// Unit tests for the two-phase second-factor verifier and its proof strategies.
/// </summary>
public class SecondFactorVerifierTests
{
    private const string ProtectedSecret = "v2:protected-secret";
    private const string PlainSecret = "PLAINBASE32SECRET";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<ITwoFactorStateStore> _stateStore = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<ITwoFactorSecretProtector> _secretProtector = new();
    private readonly SecondFactorVerifier _verifier;

    public SecondFactorVerifierTests()
    {
        _secretProtector
            .Setup(p => p.UnprotectAsync(_userId, ProtectedSecret, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlainSecret);

        _verifier = new SecondFactorVerifier(
            _stateStore.Object,
            [new TotpProofStrategy(_totp.Object, _secretProtector.Object), new RecoveryCodeProofStrategy(_totp.Object)]);
    }

    private TwoFactorSnapshot Snapshot(
        bool isEnabled = true,
        string? recoveryCodes = null,
        DateTime? lockedUntil = null) =>
        new(_userId, ProtectedSecret, recoveryCodes, isEnabled, failedAttempts: 0, lockedUntil);

    private void GivenSnapshot(TwoFactorSnapshot? snapshot) =>
        _stateStore
            .Setup(s => s.GetSnapshotAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

    private void GivenReservation(int? failedAttempts) =>
        _stateStore
            .Setup(s => s.TryReserveAttemptAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedAttempts);

    private async Task<SecondFactorReservation> ReserveAsync(TwoFactorSnapshot snapshot)
    {
        GivenSnapshot(snapshot);
        GivenReservation(1);
        var reservation = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);
        reservation.IsError.Should().BeFalse();
        return reservation.Value;
    }

    // ── ReserveAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task LockedAccount_NoReservation()
    {
        GivenSnapshot(Snapshot(lockedUntil: DateTime.UtcNow.AddMinutes(10)));

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeTrue();
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");
        _stateStore.Verify(
            s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a factor that is already locked is refused without counting another attempt");
    }

    [Fact]
    public async Task ReserveDenied_NoVerify()
    {
        // The read saw an unlocked factor, but the reservation — which re-checks the
        // lock in the statement that counts — refused: a concurrent request reached
        // the maximum first.
        GivenSnapshot(Snapshot());
        GivenReservation(null);

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeTrue("no reservation exists, so there is nothing a code could be verified against");
        result.FirstError.Code.Should().Be("TwoFactor.LockedOut");
        _stateStore.Verify(s => s.TryReserveAttemptAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
        _totp.Verify(t => t.ValidateCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _totp.Verify(t => t.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _secretProtector.Verify(
            p => p.UnprotectAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reserve_EnabledFactor_CountsOneAttemptAndCarriesTheRowAsRead()
    {
        var snapshot = Snapshot(recoveryCodes: "[\"h1\"]");
        GivenSnapshot(snapshot);
        GivenReservation(3);

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.Value.Snapshot.Should().BeSameAs(snapshot);
        _stateStore.Verify(s => s.TryReserveAttemptAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reserve_NoFactor_ReturnsTwoFactorNotEnabled()
    {
        GivenSnapshot(null);

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
        _stateStore.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reserve_DisabledFactor_ReturnsTwoFactorNotEnabled()
    {
        GivenSnapshot(Snapshot(isEnabled: false));

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None);

        result.FirstError.Code.Should().Be("User.TwoFactorNotEnabled");
        _stateStore.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reserve_ExpectingPending_NoRow_ReturnsSetupRequired()
    {
        GivenSnapshot(null);

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: false, CancellationToken.None);

        result.FirstError.Code.Should().Be("TwoFactor.SetupRequired");
    }

    [Fact]
    public async Task Reserve_ExpectingPending_EnabledRow_ReturnsAlreadyEnabled()
    {
        GivenSnapshot(Snapshot(isEnabled: true));

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: false, CancellationToken.None);

        result.FirstError.Code.Should().Be("User.TwoFactorAlreadyEnabled");
        _stateStore.Verify(s => s.TryReserveAttemptAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reserve_ExpectingPending_PendingRow_Reserves()
    {
        GivenSnapshot(Snapshot(isEnabled: false));
        GivenReservation(1);

        var result = await _verifier.ReserveAsync(_userId, expectEnabled: false, CancellationToken.None);

        result.IsError.Should().BeFalse();
        _stateStore.Verify(s => s.TryReserveAttemptAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── VerifyAsync: TOTP ──────────────────────────────────────────────────

    [Fact]
    public async Task VerifyTotp_DecryptsTheStoredSecretAndProvesTheCode()
    {
        var reservation = await ReserveAsync(Snapshot());
        _totp.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(true);

        var proof = await _verifier.VerifyAsync(reservation, "123456", SecondFactorMethod.Totp, CancellationToken.None);

        proof.IsError.Should().BeFalse();
        proof.Value.Method.Should().Be(SecondFactorMethod.Totp);
        proof.Value.Step.Should().BeNull("no commit claims time steps yet");
        proof.Value.OldCodesJson.Should().BeNull();
        proof.Value.NewCodesJson.Should().BeNull();
        _secretProtector.Verify(p => p.UnprotectAsync(_userId, ProtectedSecret, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyTotp_WrongCode_ReturnsInvalidTwoFactorCode()
    {
        var reservation = await ReserveAsync(Snapshot());
        _totp.Setup(t => t.ValidateCode(PlainSecret, "000000")).Returns(false);

        var proof = await _verifier.VerifyAsync(reservation, "000000", SecondFactorMethod.Totp, CancellationToken.None);

        proof.FirstError.Code.Should().Be("User.InvalidTwoFactorCode");
    }

    [Fact]
    public async Task Verify_WritesNothing()
    {
        var reservation = await ReserveAsync(Snapshot(recoveryCodes: "[\"h1\"]"));
        _stateStore.Invocations.Clear();
        _totp.Setup(t => t.ValidateCode(PlainSecret, "123456")).Returns(true);
        _totp.Setup(t => t.VerifyRecoveryCode("AAAA-BBBB", "h1")).Returns(true);

        await _verifier.VerifyAsync(reservation, "123456", SecondFactorMethod.Totp, CancellationToken.None);
        await _verifier.VerifyAsync(reservation, "AAAA-BBBB", SecondFactorMethod.RecoveryCode, CancellationToken.None);

        _stateStore.Invocations.Should().BeEmpty(
            "verification only computes; the caller's commit is the one write that makes a proof count");
    }

    // ── VerifyAsync: recovery codes ────────────────────────────────────────

    [Fact]
    public async Task RecoveryProof_CarriesOldAndNew()
    {
        // Deliberately not the canonical form JsonSerializer writes: the commit
        // compares the row against this text, so it must reach the proof untouched.
        const string loaded = "[ \"h1\", \"h2\" ]";
        var reservation = await ReserveAsync(Snapshot(recoveryCodes: loaded));
        _totp.Setup(t => t.VerifyRecoveryCode("AAAA-BBBB", It.IsAny<string>()))
            .Returns<string, string>((_, hash) => hash == "h1");

        var proof = await _verifier.VerifyAsync(reservation, "AAAA-BBBB", SecondFactorMethod.RecoveryCode, CancellationToken.None);

        proof.IsError.Should().BeFalse();
        proof.Value.Method.Should().Be(SecondFactorMethod.RecoveryCode);
        proof.Value.OldCodesJson.Should().Be(loaded, "the stored text byte for byte, never a re-serialization");
        JsonSerializer.Deserialize<List<string>>(proof.Value.NewCodesJson!).Should().Equal("h2");
    }

    [Fact]
    public async Task VerifyRecovery_LastCode_LeavesAnEmptySet()
    {
        var reservation = await ReserveAsync(Snapshot(recoveryCodes: "[\"h1\"]"));
        _totp.Setup(t => t.VerifyRecoveryCode("AAAA-BBBB", "h1")).Returns(true);

        var proof = await _verifier.VerifyAsync(reservation, "AAAA-BBBB", SecondFactorMethod.RecoveryCode, CancellationToken.None);

        proof.Value.NewCodesJson.Should().Be("[]");
    }

    [Fact]
    public async Task VerifyRecovery_NoMatch_ReturnsInvalidRecoveryCode()
    {
        var reservation = await ReserveAsync(Snapshot(recoveryCodes: "[\"h1\"]"));
        _totp.Setup(t => t.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>())).Returns(false);

        var proof = await _verifier.VerifyAsync(reservation, "XXXX-YYYY", SecondFactorMethod.RecoveryCode, CancellationToken.None);

        proof.FirstError.Code.Should().Be("TwoFactor.InvalidRecoveryCode");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    public async Task VerifyRecovery_NoStoredCodes_ReturnsNoRecoveryCodesAvailable(string? stored)
    {
        var reservation = await ReserveAsync(Snapshot(recoveryCodes: stored));

        var proof = await _verifier.VerifyAsync(reservation, "AAAA-BBBB", SecondFactorMethod.RecoveryCode, CancellationToken.None);

        proof.FirstError.Code.Should().Be("TwoFactor.NoRecoveryCodesAvailable");
        _totp.Verify(t => t.VerifyRecoveryCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── Strategy registry ──────────────────────────────────────────────────

    [Fact]
    public async Task Verify_MethodWithoutStrategy_Throws()
    {
        var verifier = new SecondFactorVerifier(_stateStore.Object, [new RecoveryCodeProofStrategy(_totp.Object)]);
        GivenSnapshot(Snapshot());
        GivenReservation(1);
        var reservation = (await verifier.ReserveAsync(_userId, expectEnabled: true, CancellationToken.None)).Value;

        var act = () => verifier.VerifyAsync(reservation, "123456", SecondFactorMethod.Totp, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void TwoStrategiesForOneMethod_AreRefused()
    {
        var act = () => new SecondFactorVerifier(
            _stateStore.Object,
            [new RecoveryCodeProofStrategy(_totp.Object), new RecoveryCodeProofStrategy(_totp.Object)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Proofs_KeepTheCodeSetsOutOfTheirText()
    {
        var proof = SecondFactorProof.RecoveryCode("[\"secret-hash-1\"]", "[]");
        var snapshot = new TwoFactorSnapshot(_userId, ProtectedSecret, "[\"secret-hash-1\"]", true, 0, null);

        proof.ToString().Should().NotContain("secret-hash-1");
        snapshot.ToString().Should().NotContain("secret-hash-1").And.NotContain(ProtectedSecret);
    }
}
