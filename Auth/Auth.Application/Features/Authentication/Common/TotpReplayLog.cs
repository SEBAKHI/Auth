namespace Auth.Application.Features.Authentication.Common;

/// <summary>
/// The two warning lines a reused authenticator-app code leaves, worded once.
/// While the rollout switch exists an operator reviews them by searching for this
/// exact text, so every place that claims a code's step writes the same line —
/// never a near copy that the search would miss.
/// </summary>
internal static class TotpReplayLog
{
    /// <summary>The surfaces that claim a code's step, as the lines name them.</summary>
    public const string SignIn = "sign-in";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string AccountRecovery = "account-recovery";
    public const string StepUp = "step-up";

    /// <summary>
    /// A correct code was refused because its step was already accepted. The
    /// account and the surface, never the code.
    /// </summary>
    public static void ReusedCodeRejected(this ILogger logger, Guid userId, string surface, string? ipAddress) =>
        logger.LogWarning(
            "Reused two-factor code rejected for user {UserId} on {Surface} from {IpAddress}",
            userId, surface, ipAddress);

    /// <summary>
    /// A code whose step was already accepted was let through, because
    /// TwoFactor:RejectReusedCodes is off. The account and the surface, never
    /// the code.
    /// </summary>
    public static void ReusedCodeAccepted(this ILogger logger, Guid userId, string surface, string? ipAddress) =>
        logger.LogWarning(
            "Reused two-factor code accepted (RejectReusedCodes=false) for user {UserId} on {Surface} from {IpAddress}",
            userId, surface, ipAddress);
}
