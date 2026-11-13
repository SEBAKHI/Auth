using System.Collections.Frozen;

namespace Auth.Application.Common.BotDefense;

/// <summary>
/// The action each protected surface asks the widget for, and expects back in Cloudflare's
/// answer: a token made for one surface is refused on another. Turnstile accepts at most 32
/// characters of <c>[A-Za-z0-9_-]</c>; the apps send the same words.
/// </summary>
public static class BotDefenseActions
{
    /// <summary><c>POST api/v1/Auth/login</c>: the password sign-in.</summary>
    public const string Login = "login";

    /// <summary><c>POST api/v1/Auth/registration/start</c>: the address-only sign-up start.</summary>
    public const string Register = "register";

    /// <summary><c>POST api/v1/Auth/forgot-password</c>: the anonymous reset request.</summary>
    public const string ForgotPassword = "forgot-password";

    /// <summary><c>POST api/v1/Auth/email-code/start</c>: the code that signs in or signs up.</summary>
    public const string EmailCode = "email-code";

    /// <summary>Every protected surface's action.</summary>
    public static readonly IReadOnlyList<string> All = [Login, Register, ForgotPassword, EmailCode];

    /// <summary>
    /// The surfaces that, under <c>FailMode = PerSurface</c>, go on through the per-address
    /// allowance while the check cannot run: the ways back into an existing account (D-S13-1).
    /// A sign-up start is refused instead: nobody is locked out of an account that does not
    /// exist yet.
    /// </summary>
    public static readonly FrozenSet<string> ThroughAllowanceWhenUnavailable =
        new[] { Login, ForgotPassword, EmailCode }.ToFrozenSet(StringComparer.Ordinal);
}
