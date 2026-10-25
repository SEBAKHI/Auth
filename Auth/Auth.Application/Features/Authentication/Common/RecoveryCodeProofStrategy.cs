using System.Text.Json;
using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Checks one of the user's single-use recovery codes against the stored hashes.
/// </summary>
/// <remarks>
/// A recovery code is the way out for either second factor, so it is accepted on a
/// row whose factor is the authenticator app or the emailed code. What it proves
/// depends on how the set was issued (FA3): with the authenticator app or in a
/// session that proved two factors, a second factor; when the emailed code was
/// switched on, after the password alone, nothing a policy counts. An enabled
/// authenticator app's codes always count: no path stores any other set beside it,
/// so the column is read only on a row whose factor is the emailed code — which
/// also keeps a row the previous build enabled, before the column was qualified,
/// from losing what its codes prove.
/// </remarks>
public class RecoveryCodeProofStrategy : ISecondFactorProofStrategy
{
    private readonly ITotpService _totpService;

    public RecoveryCodeProofStrategy(ITotpService totpService)
    {
        _totpService = totpService;
    }

    /// <inheritdoc />
    public SecondFactorMethod Method => SecondFactorMethod.RecoveryCode;

    /// <inheritdoc />
    public SecondFactorCounter Counter => SecondFactorCounter.Shared;

    /// <inheritdoc />
    public bool IsBound(TwoFactorSnapshot snapshot) => snapshot.HasSecondFactor;

    /// <inheritdoc />
    public ErrorOr<Success> Screen(SecondFactorPresentation presentation) => Result.Success;

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        CancellationToken cancellationToken) =>
        Task.FromResult(Verify(reservation.Snapshot, code));

    private ErrorOr<SecondFactorProof> Verify(TwoFactorSnapshot snapshot, string code)
    {
        var storedCodes = snapshot.RecoveryCodes;
        if (string.IsNullOrWhiteSpace(storedCodes))
        {
            return TwoFactorErrors.NoRecoveryCodesAvailable;
        }

        var hashes = JsonSerializer.Deserialize<List<string>>(storedCodes);
        if (hashes == null || hashes.Count == 0)
        {
            return TwoFactorErrors.NoRecoveryCodesAvailable;
        }

        var matched = hashes.FirstOrDefault(hash => _totpService.VerifyRecoveryCode(code, hash));
        if (matched == null)
        {
            return TwoFactorErrors.InvalidRecoveryCode;
        }

        hashes.Remove(matched);

        // The stored text goes into the proof untouched. The commit spends the code
        // by comparing the row against it, and a re-serialization of the same set
        // could differ in spacing and never compare equal.
        return SecondFactorProof.RecoveryCode(
            storedCodes,
            JsonSerializer.Serialize(hashes),
            qualified: snapshot.IsEnabled || snapshot.RecoveryCodesQualified);
    }
}
