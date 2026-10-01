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
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        TwoFactorSnapshot snapshot,
        string code,
        CancellationToken cancellationToken) =>
        Task.FromResult(Verify(snapshot.RecoveryCodes, code));

    private ErrorOr<SecondFactorProof> Verify(string? storedCodes, string code)
    {
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
        return SecondFactorProof.RecoveryCode(storedCodes, JsonSerializer.Serialize(hashes));
    }
}
