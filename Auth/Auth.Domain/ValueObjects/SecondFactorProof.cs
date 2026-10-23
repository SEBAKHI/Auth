using Auth.Domain.Enums;

namespace Auth.Domain.ValueObjects;

/// <summary>
/// What a correct second-factor code proved, carried from the check — which may
/// compute a TOTP code or hash recovery codes, but writes nothing — to the one
/// conditional commit that makes it count.
/// </summary>
/// <remarks>
/// A recovery-code proof carries the stored code set twice: exactly as it was
/// loaded, and without the code that matched. The commit replaces the set only
/// while the row still holds the loaded text, so one code presented on two
/// challenges at the same instant is spent once and signs in once.
/// <para>
/// A TOTP proof carries the time step its code matched. The commit claims that
/// step, and only while it is newer than the last one accepted, so a code is
/// accepted once however many times, or on however many challenges, it is
/// presented.
/// </para>
/// <para>
/// An emailed-code proof carries the challenge the code was emailed for and that
/// challenge's code hashes exactly as read. The commit consumes the challenge, so
/// the code signs in once, and only while no newer code was emailed meanwhile.
/// </para>
/// </remarks>
public sealed record class SecondFactorProof
{
    private SecondFactorProof(
        SecondFactorMethod method,
        AuthenticationMethods methods,
        long? step,
        string? oldCodesJson,
        string? newCodesJson,
        Guid? challengeId,
        string? emailCodesSeen)
    {
        Method = method;
        Methods = methods;
        Step = step;
        OldCodesJson = oldCodesJson;
        NewCodesJson = newCodesJson;
        ChallengeId = challengeId;
        EmailCodesSeen = emailCodesSeen;
    }

    /// <summary>
    /// Gets the factor the code was presented as.
    /// </summary>
    public SecondFactorMethod Method { get; }

    /// <summary>
    /// Gets what the code proved, for the session it signs in: the authenticator app;
    /// a recovery code only when the set was issued with two factors (FA3), otherwise
    /// nothing; an emailed code recorded as such, which no policy counts.
    /// </summary>
    public AuthenticationMethods Methods { get; }

    /// <summary>
    /// Gets the absolute TOTP time step (Unix seconds / 30) the code matched,
    /// which the commit claims. Null for every other proof.
    /// </summary>
    public long? Step { get; }

    /// <summary>
    /// Gets the stored recovery-code set byte for byte as it was loaded — never a
    /// re-serialization, which could differ in spacing and never compare equal.
    /// Null for every other proof.
    /// </summary>
    public string? OldCodesJson { get; }

    /// <summary>
    /// Gets the recovery-code set without the code that matched. Null for every other proof.
    /// </summary>
    public string? NewCodesJson { get; }

    /// <summary>
    /// Gets the challenge the matched code was emailed for. Null for every other proof.
    /// </summary>
    public Guid? ChallengeId { get; }

    /// <summary>
    /// Gets that challenge's code hashes exactly as read. The commit spends the
    /// challenge only while they are unchanged, so a code that a newer send replaced
    /// after it was checked signs nobody in. Null for every other proof.
    /// </summary>
    public string? EmailCodesSeen { get; }

    /// <summary>
    /// Creates the proof of a correct authenticator-app code.
    /// </summary>
    /// <param name="step">The absolute time step the code matched.</param>
    public static SecondFactorProof Totp(long step)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        return new SecondFactorProof(
            SecondFactorMethod.Totp, AuthenticationMethods.Totp, step, null, null, null, null);
    }

    /// <summary>
    /// Creates the proof of a correct recovery code.
    /// </summary>
    /// <param name="oldCodesJson">The stored code set exactly as loaded.</param>
    /// <param name="newCodesJson">The code set without the matched code.</param>
    /// <param name="qualified">
    /// Whether the set was issued with the authenticator app or in a session that
    /// proved two factors (<c>TwoFactorAuth.RecoveryCodesQualified</c>). An
    /// unqualified code still completes the step it is presented for, but the session
    /// records no second factor: codes handed out after the password alone would
    /// otherwise turn one factor into two.
    /// </param>
    public static SecondFactorProof RecoveryCode(string oldCodesJson, string newCodesJson, bool qualified = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldCodesJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(newCodesJson);

        return new SecondFactorProof(
            SecondFactorMethod.RecoveryCode,
            qualified ? AuthenticationMethods.RecoveryCode : AuthenticationMethods.Unknown,
            null,
            oldCodesJson,
            newCodesJson,
            null,
            null);
    }

    /// <summary>
    /// Creates the proof of a correct emailed code.
    /// </summary>
    /// <param name="challengeId">The challenge the code was emailed for.</param>
    /// <param name="emailCodesSeen">That challenge's code hashes exactly as read.</param>
    public static SecondFactorProof EmailCode(Guid challengeId, string emailCodesSeen)
    {
        if (challengeId == Guid.Empty)
        {
            throw new ArgumentException("An emailed-code proof names its challenge.", nameof(challengeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(emailCodesSeen);

        return new SecondFactorProof(
            SecondFactorMethod.EmailCode, AuthenticationMethods.EmailCode, null, null, null, challengeId, emailCodesSeen);
    }

    // The code sets are hashes of single-use secrets. The synthesized ToString
    // would print them into any log line or assertion message the proof reaches.
    public override string ToString() => $"SecondFactorProof {{ Method = {Method} }}";
}
