using System.Collections.Frozen;
using Auth.Domain.Enums;

namespace Auth.Application.Features.Authentication.Authorize;

/// <summary>
/// The <c>reason</c> word the authorize endpoint adds to the accounts sign-in page when an
/// application's two-step requirement sends the person there (S23, AM7 (7-3)), and the
/// OAuth error description of the loop guard's refusal.
/// </summary>
/// <remarks>
/// Short words, never an address: the accounts URL must stay something a web application
/// firewall reads as nothing (OI-145). The word is a display hint only. The page shows a
/// message for it and the resume recomputes the requirement from the session, so a
/// forged or dropped word changes what the person reads, never what the server requires.
/// </remarks>
public static class ApplicationMfaReason
{
    /// <summary>The accounts page parameter.</summary>
    public const string QueryName = "reason";

    /// <summary>
    /// The <c>error_description</c> of the loop guard's <c>access_denied</c> (D-T4 A): a
    /// relying party's OpenID Connect handler sends it to its access-denied page.
    /// </summary>
    public const string GuardErrorDescription = "application_requires_two_step_verification";

    private static readonly FrozenDictionary<ApplicationMfaRequirement, string> Words =
        new Dictionary<ApplicationMfaRequirement, string>
        {
            [ApplicationMfaRequirement.StepUp] = "mfa_step_up",
            [ApplicationMfaRequirement.Passkey] = "mfa_passkey",
            [ApplicationMfaRequirement.Enroll] = "mfa_enroll",
            [ApplicationMfaRequirement.Reauthenticate] = "mfa_reauthenticate",
        }.ToFrozenDictionary();

    /// <summary>The word for <paramref name="requirement"/>; none has a word for <see cref="ApplicationMfaRequirement.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A requirement that sends nobody anywhere: a programming error.</exception>
    public static string For(ApplicationMfaRequirement requirement) =>
        Words.TryGetValue(requirement, out var word)
            ? word
            : throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "No sign-in reason for this requirement.");
}
