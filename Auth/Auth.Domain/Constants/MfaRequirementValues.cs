using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Domain.Constants;

/// <summary>
/// The wire vocabulary of <see cref="MfaRequirement"/>: the value of the
/// <c>mfa_req</c> claim, and of <c>mfaRequirement</c> in the user info a sign-in
/// and <c>/auth/me</c> return. The clients key on these strings.
/// </summary>
public static class MfaRequirementValues
{
    public const string None = "none";
    public const string Enroll = "enroll";
    public const string StepUp = "step_up";
    public const string Reauthenticate = "reauthenticate";

    private static readonly FrozenDictionary<MfaRequirement, string> ByRequirement =
        new Dictionary<MfaRequirement, string>
        {
            [MfaRequirement.None] = None,
            [MfaRequirement.Enroll] = Enroll,
            [MfaRequirement.StepUp] = StepUp,
            [MfaRequirement.Reauthenticate] = Reauthenticate,
        }.ToFrozenDictionary();

    /// <summary>The claim value of a requirement.</summary>
    public static string ToClaimValue(this MfaRequirement requirement) =>
        ByRequirement.TryGetValue(requirement, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "A requirement with no claim value.");
}
