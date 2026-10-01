using Auth.Application.Interfaces;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Checks a code from the user's authenticator app.
/// </summary>
public class TotpProofStrategy : ISecondFactorProofStrategy
{
    private readonly ITotpService _totpService;
    private readonly ITwoFactorSecretProtector _secretProtector;

    public TotpProofStrategy(ITotpService totpService, ITwoFactorSecretProtector secretProtector)
    {
        _totpService = totpService;
        _secretProtector = secretProtector;
    }

    /// <inheritdoc />
    public SecondFactorMethod Method => SecondFactorMethod.Totp;

    /// <inheritdoc />
    public async Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        TwoFactorSnapshot snapshot,
        string code,
        CancellationToken cancellationToken)
    {
        // Decrypted only here, for the one comparison that needs it, so the
        // plaintext secret never travels with the reservation.
        var secret = await _secretProtector.UnprotectAsync(
            snapshot.UserId, snapshot.ProtectedSecretKey, cancellationToken);

        if (!_totpService.ValidateCode(secret, code))
        {
            return UserErrors.InvalidTwoFactorCode;
        }

        return SecondFactorProof.Totp();
    }
}
