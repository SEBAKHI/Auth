using Auth.Application.Configuration;

namespace Auth.Application.Security.Keyring;

/// <summary>
/// How long a signing key that stops signing is still accepted (S12, KR4, T12): the one
/// function every promote computes the window with, on the user revocation horizon
/// (<see cref="RevocationHorizon.User"/>, X04), so nothing else may compute this window.
/// </summary>
public static class SigningKeyOverlap
{
    /// <summary>
    /// The last instant a token signed by the key promoted away at <paramref name="promotedAtUtc"/>
    /// is accepted: <c>t_promote + RevocationHorizon.User</c> (24 h 5 min), the longest access-token
    /// lifetime plus the largest clock skew, both bounded where they are read. Every token the old
    /// key signed has expired by then, since it stopped signing at <paramref name="promotedAtUtc"/>.
    /// </summary>
    public static DateTime PreviousVerifyUntil(DateTime promotedAtUtc) =>
        promotedAtUtc + RevocationHorizon.User;
}
