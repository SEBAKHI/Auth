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
/// </remarks>
public sealed record class SecondFactorProof
{
    private SecondFactorProof(SecondFactorMethod method, string? oldCodesJson, string? newCodesJson)
    {
        Method = method;
        OldCodesJson = oldCodesJson;
        NewCodesJson = newCodesJson;
    }

    /// <summary>
    /// Gets the factor the code was presented as.
    /// </summary>
    public SecondFactorMethod Method { get; }

    /// <summary>
    /// Gets the TOTP time step the code matched. Always null today: no commit
    /// claims time steps yet, so a TOTP proof carries its method alone.
    /// </summary>
    public long? Step { get; }

    /// <summary>
    /// Gets the stored recovery-code set byte for byte as it was loaded — never a
    /// re-serialization, which could differ in spacing and never compare equal.
    /// Null for a TOTP proof.
    /// </summary>
    public string? OldCodesJson { get; }

    /// <summary>
    /// Gets the recovery-code set without the code that matched. Null for a TOTP proof.
    /// </summary>
    public string? NewCodesJson { get; }

    /// <summary>
    /// Creates the proof of a correct authenticator-app code.
    /// </summary>
    public static SecondFactorProof Totp() => new(SecondFactorMethod.Totp, null, null);

    /// <summary>
    /// Creates the proof of a correct recovery code.
    /// </summary>
    /// <param name="oldCodesJson">The stored code set exactly as loaded.</param>
    /// <param name="newCodesJson">The code set without the matched code.</param>
    public static SecondFactorProof RecoveryCode(string oldCodesJson, string newCodesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldCodesJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(newCodesJson);

        return new SecondFactorProof(SecondFactorMethod.RecoveryCode, oldCodesJson, newCodesJson);
    }

    // The code sets are hashes of single-use secrets. The synthesized ToString
    // would print them into any log line or assertion message the proof reaches.
    public override string ToString() => $"SecondFactorProof {{ Method = {Method} }}";
}
