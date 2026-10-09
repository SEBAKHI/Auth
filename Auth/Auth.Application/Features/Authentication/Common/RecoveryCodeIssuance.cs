using System.Text.Json;
using Auth.Application.Interfaces;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The one way a set of recovery codes is issued — when the factor is switched
/// on, when the codes are regenerated, when the authenticator is replaced: the
/// same number of codes, hashed the same way, before any transaction opens.
/// </summary>
internal static class RecoveryCodeIssuance
{
    /// <summary>How many codes a set holds.</summary>
    public const int CodeCount = 10;

    /// <summary>
    /// Generates a new set, and the JSON array of their hashes the factor stores.
    /// No hashing waits inside a transaction: the caller writes the JSON afterwards.
    /// </summary>
    public static (string[] Codes, string HashedJson) IssueRecoveryCodes(this ITotpService totpService)
    {
        var codes = totpService.GenerateRecoveryCodes(CodeCount);
        var hashedJson = JsonSerializer.Serialize(
            codes.Select(code => totpService.HashRecoveryCode(code)).ToArray());

        return (codes, hashedJson);
    }
}
