namespace Auth.Application.Security.Keyring;

/// <summary>
/// How long a signing key that stops signing is still accepted (S12, KR4, T12): the one
/// function every promote computes the window with. X04 replaces its body with the user
/// revocation horizon (RevocationHorizon.User), so nothing else may compute this window.
/// </summary>
public static class SigningKeyOverlap
{
    /// <summary>The shortest window: one day, whatever the access-token lifetime.</summary>
    public static readonly TimeSpan MinimumWindow = TimeSpan.FromMinutes(1440);

    /// <summary>The margin for clock skew between this server and the parties that validate its tokens.</summary>
    public static readonly TimeSpan SkewMargin = TimeSpan.FromSeconds(300);

    /// <summary>
    /// The last instant a token signed by the key promoted away at <paramref name="promotedAtUtc"/>
    /// is accepted: <c>t_promote + max(1440 min, L) + 300 s</c>, with L the access-token lifetime
    /// read when the promote runs. Every token the old key signed has expired by then, since it
    /// stopped signing at <paramref name="promotedAtUtc"/>.
    /// </summary>
    public static DateTime PreviousVerifyUntil(DateTime promotedAtUtc, TimeSpan accessTokenLifetime) =>
        promotedAtUtc + (accessTokenLifetime > MinimumWindow ? accessTokenLifetime : MinimumWindow) + SkewMargin;
}
