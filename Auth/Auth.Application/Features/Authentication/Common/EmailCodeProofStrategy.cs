using Auth.Application.Interfaces;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using ErrorOr;

namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// Checks a code emailed for one challenge, on an account whose second factor is the
/// emailed code (S24).
/// </summary>
/// <remarks>
/// <para>
/// A code is hashed with its challenge's id in the scope, so it verifies against that
/// challenge alone: a code emailed for another sign-in, or another account, is just
/// wrong here. Its failures count against the emailed code's own counter.
/// </para>
/// <para>
/// Two refusals cost no attempt. A code this challenge emailed before a newer send
/// replaced it: only the holder of the mailbox could present it, so it is no guess,
/// and counting it would lock the owner out for reading the wrong email. And any
/// emailed code after a first factor other than the password: a provider sign-in or
/// a verify-email link already went through the same mailbox (D-T35 A), so a code
/// sent there proves nothing new.
/// </para>
/// <para>
/// What it proves is recorded as <see cref="AuthenticationMethods.EmailCode"/>, which
/// no two-factor policy counts: whoever holds the mailbox already holds the password
/// reset (NIST SP 800-63B §3.1.3.1).
/// </para>
/// </remarks>
public class EmailCodeProofStrategy : ISecondFactorProofStrategy
{
    /// <summary>
    /// The scope prefix a code is hashed under, before the challenge id. Public
    /// because the OTP hasher fingerprints a scope with this prefix under its own
    /// credential purpose (TwoFactorEmailCode).
    /// </summary>
    public const string ScopePrefix = "two-factor-email:";

    private readonly IOtpHasher _otpHasher;

    public EmailCodeProofStrategy(IOtpHasher otpHasher)
    {
        _otpHasher = otpHasher;
    }

    /// <inheritdoc />
    public SecondFactorMethod Method => SecondFactorMethod.EmailCode;

    /// <inheritdoc />
    public SecondFactorCounter Counter => SecondFactorCounter.EmailCode;

    /// <summary>
    /// The scope a code emailed for <paramref name="challengeId"/> is hashed and
    /// verified under.
    /// </summary>
    public static string HashScope(Guid challengeId) => ScopePrefix + challengeId;

    /// <summary>
    /// Whether an emailed code may complete a challenge opened by
    /// <paramref name="primaryMethod"/>: only one opened by the password alone.
    /// </summary>
    public static bool MayFollow(AuthenticationMethods primaryMethod) =>
        primaryMethod == AuthenticationMethods.Password;

    /// <inheritdoc />
    public bool IsBound(TwoFactorSnapshot snapshot) => snapshot.HasEmailCode;

    /// <inheritdoc />
    public ErrorOr<Success> Screen(SecondFactorPresentation presentation)
    {
        if (presentation.Challenge is not { } challenge || !MayFollow(challenge.PrimaryMethod))
        {
            return TwoFactorErrors.EmailCodeUnavailable;
        }

        return challenge.SupersededEmailCodeHashes.Any(
            hash => _otpHasher.Verify(HashScope(challenge.Id), presentation.Code, hash))
            ? TwoFactorErrors.EmailCodeSuperseded
            : Result.Success;
    }

    /// <inheritdoc />
    public Task<ErrorOr<SecondFactorProof>> VerifyAsync(
        SecondFactorReservation reservation,
        string code,
        CancellationToken cancellationToken) =>
        Task.FromResult(Verify(reservation.Challenge, code));

    /// <summary>
    /// Whether <paramref name="code"/> is the live code — the latest one emailed —
    /// of <paramref name="challenge"/>. The one comparison every emailed code goes
    /// through: at sign-in, and when the emailed code is switched on (S24).
    /// </summary>
    public static bool IsLiveCode(IOtpHasher otpHasher, TwoFactorChallenge? challenge, string code) =>
        // No code sent yet, or no challenge to send one for: nothing can match.
        challenge?.LiveEmailCodeHash is { } live
        && otpHasher.Verify(HashScope(challenge.Id), code, live);

    private ErrorOr<SecondFactorProof> Verify(TwoFactorChallenge? challenge, string code)
    {
        if (!IsLiveCode(_otpHasher, challenge, code))
        {
            return UserErrors.InvalidTwoFactorCode;
        }

        return SecondFactorProof.EmailCode(challenge!.Id, challenge.EmailCodeHash!);
    }
}
