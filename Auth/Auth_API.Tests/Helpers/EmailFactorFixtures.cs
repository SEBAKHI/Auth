using Auth.Application.Features.Authentication.Common;
using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// What the emailed second factor's tests share (S24): an OTP hasher whose stored form
/// shows the scope it was computed under, and the rows of an account whose factor is
/// the emailed code.
/// </summary>
internal static class EmailFactorFixtures
{
    /// <summary>The stored form of <paramref name="code"/> emailed for <paramref name="challengeId"/>.</summary>
    public static string MacOf(Guid challengeId, string code) =>
        ScopedOtpHasher.Instance.Hash(EmailCodeProofStrategy.HashScope(challengeId), code);

    /// <summary>A live sign-in (or recovery) challenge that emailed <paramref name="codes"/>, oldest first.</summary>
    public static TwoFactorChallenge ChallengeWith(
        Guid userId,
        AuthenticationMethods primary,
        TwoFactorChallengePurpose purpose = TwoFactorChallengePurpose.SignIn,
        params string[] codes)
    {
        var id = Guid.NewGuid();
        var hashes = codes.Length == 0 ? null : string.Join(';', codes.Select(code => MacOf(id, code)));

        return new TwoFactorChallenge(
            id, userId, "token-hash", "203.0.113.7", DateTime.UtcNow.AddMinutes(10), usedAt: null, attemptCount: 0,
            DateTime.UtcNow.AddMinutes(-1), primary, purpose, hashes, codes.Length == 0 ? null : DateTime.UtcNow, codes.Length);
    }

    /// <summary>The row of an account whose second factor is the emailed code.</summary>
    public static TwoFactorSnapshot EmailRow(
        Guid userId,
        string? recoveryCodes = null,
        int emailFailedAttempts = 0,
        DateTime? emailLockedUntil = null,
        bool recoveryCodesQualified = false) =>
        new(userId, protectedSecretKey: null, recoveryCodes, isEnabled: false, failedAttempts: 0, lockedUntil: null,
            modifiedAt: DateTime.UtcNow.AddDays(-1), emailCodeEnabledAt: DateTime.UtcNow.AddDays(-30),
            emailFailedAttempts: emailFailedAttempts, emailLockedUntil: emailLockedUntil,
            recoveryCodesQualified: recoveryCodesQualified);

    /// <summary>The row of an account whose second factor is the authenticator app.</summary>
    public static TwoFactorSnapshot TotpRow(Guid userId, string? recoveryCodes = null) =>
        new(userId, "v2:ciphertext", recoveryCodes, isEnabled: true, failedAttempts: 0, lockedUntil: null,
            recoveryCodesQualified: true);
}

/// <summary>
/// An OTP hasher whose stored form names the scope: a code hashed for one challenge never
/// verifies against another, exactly as with the keyed hasher, and a test can read why.
/// </summary>
internal sealed class ScopedOtpHasher : IOtpHasher
{
    public static ScopedOtpHasher Instance { get; } = new();

    public string Hash(string scope, string code) => $"mac({scope}:{code})";

    public bool Verify(string scope, string code, string storedHash) => storedHash == Hash(scope, code);
}
