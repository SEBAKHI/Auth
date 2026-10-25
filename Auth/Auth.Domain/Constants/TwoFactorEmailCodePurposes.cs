using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Domain.Constants;

/// <summary>
/// The values of the <c>Purpose</c> variable of the
/// <see cref="NotificationTypeCodes.TwoFactorEmailCode"/> email: what the code was
/// sent for (S24). Its templates branch on these exact strings, so they are declared
/// once, never localized, and never free text.
/// </summary>
public static class TwoFactorEmailCodePurposes
{
    /// <summary>The second step of a sign-in.</summary>
    public const string SignIn = "sign-in";

    /// <summary>Switching the emailed code on, from a signed-in session.</summary>
    public const string Enrollment = "enrollment";

    /// <summary>Switching the emailed code off, or replacing it by an app, from a signed-in session.</summary>
    public const string SwitchOff = "switch-off";

    /// <summary>The recovery of an account waiting for deletion.</summary>
    public const string DeletionRecovery = "deletion-recovery";

    private static readonly FrozenDictionary<TwoFactorChallengePurpose, string> ByChallengePurpose =
        new Dictionary<TwoFactorChallengePurpose, string>
        {
            [TwoFactorChallengePurpose.SignIn] = SignIn,
            [TwoFactorChallengePurpose.EmailEnrollment] = Enrollment,
            [TwoFactorChallengePurpose.EmailDisable] = SwitchOff,
            [TwoFactorChallengePurpose.DeletionRecovery] = DeletionRecovery,
        }.ToFrozenDictionary();

    /// <summary>Every value, in the order the templates word them.</summary>
    public static IReadOnlyList<string> All { get; } = [SignIn, Enrollment, SwitchOff, DeletionRecovery];

    /// <summary>The value for a challenge's purpose.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A purpose with no emailed code.</exception>
    public static string Of(TwoFactorChallengePurpose purpose) =>
        ByChallengePurpose.TryGetValue(purpose, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "No emailed code is sent for this purpose.");
}
