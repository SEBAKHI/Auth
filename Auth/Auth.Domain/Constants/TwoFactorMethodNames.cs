using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Domain.Constants;

/// <summary>
/// The wire names of the second factors a sign-in can be completed with: the values
/// of the verify request's <c>method</c> and of the login response's
/// <c>twoFactorMethods</c> (S24, EO7).
/// </summary>
/// <remarks>
/// Fixed strings, not the enum's member names, so renaming a member never changes the
/// contract. A client reads an unknown name as one it cannot offer.
/// </remarks>
public static class TwoFactorMethodNames
{
    /// <summary>A code from the authenticator app.</summary>
    public const string Totp = "totp";

    /// <summary>One of the account's recovery codes.</summary>
    public const string Recovery = "recovery";

    /// <summary>A code emailed for this sign-in, on request.</summary>
    public const string Email = "email";

    private static readonly FrozenDictionary<string, SecondFactorMethod> Methods =
        new Dictionary<string, SecondFactorMethod>(StringComparer.Ordinal)
        {
            [Totp] = SecondFactorMethod.Totp,
            [Recovery] = SecondFactorMethod.RecoveryCode,
            [Email] = SecondFactorMethod.EmailCode,
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every name, in the order a client offers them.</summary>
    public static IReadOnlyList<string> All { get; } = [Totp, Recovery, Email];

    /// <summary>Whether <paramref name="name"/> is one of the names, exactly.</summary>
    public static bool IsKnown(string? name) => name is not null && Methods.ContainsKey(name);

    /// <summary>
    /// The method a name stands for, or null for a name that is none: the total form,
    /// for a value no validator has seen yet (a command's own members, its ToString).
    /// </summary>
    public static SecondFactorMethod? TryMethodOf(string? name) =>
        name is not null && Methods.TryGetValue(name, out var method) ? method : null;

    /// <summary>The factor a known name stands for.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An unknown name: the validator refuses those first.</exception>
    public static SecondFactorMethod MethodOf(string name) =>
        Methods.TryGetValue(name, out var method)
            ? method
            : throw new ArgumentOutOfRangeException(nameof(name), name, "Not a second-factor method name.");
}
