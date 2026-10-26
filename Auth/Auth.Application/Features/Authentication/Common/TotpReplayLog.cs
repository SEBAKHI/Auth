namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The warning line a reused authenticator-app code leaves, worded once.
/// Operators search for this exact text as a security signal — a code presented
/// again may have been seen over someone's shoulder or relayed by a phishing page —
/// so every place that claims a code's step writes the same line, never a near
/// copy that the search would miss.
/// </summary>
internal static class TotpReplayLog
{
    /// <summary>The surfaces that claim a code's step, as the lines name them.</summary>
    public const string SignIn = "sign-in";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string AccountRecovery = "account-recovery";
    public const string StepUp = "step-up";
    public const string RegenerateRecoveryCodes = "regenerate-recovery-codes";
    public const string ReplaceAuthenticator = "replace-authenticator";

    /// <summary>
    /// A correct code was refused because its step was already accepted. The
    /// account and the surface, never the code.
    /// </summary>
    public static void ReusedCodeRejected(this ILogger logger, Guid userId, string surface, string? ipAddress) =>
        logger.LogWarning(
            "Reused two-factor code rejected for user {UserId} on {Surface} from {IpAddress}",
            userId, surface, ipAddress);
}
