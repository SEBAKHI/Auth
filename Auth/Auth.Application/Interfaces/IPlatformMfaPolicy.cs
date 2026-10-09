using Auth.Domain.Enums;
using Auth.Domain.ValueObjects;

namespace Auth.Application.Interfaces;

/// <summary>
/// The one decision on whether a platform administrator's token carries platform
/// authority: <c>TwoFactor:EnforceForPlatformAdmins</c>. Made at both mint sites —
/// every sign-in exit and every refresh — and nowhere else.
/// </summary>
/// <remarks>
/// A platform administrator is whoever the platform token would carry at least
/// one permission for (§11 q4: every holder of an active platform permission).
/// Application tokens, organization permissions (<c>org_perm</c>) and accounts
/// without platform permissions are never touched: two-step verification stays
/// optional for them.
/// </remarks>
public interface IPlatformMfaPolicy
{
    /// <summary>
    /// The pure decision, in a fixed order. No platform permission: nothing
    /// required. No enabled second factor on the account: enroll — before the
    /// session is looked at, so a session that proved a factor since removed loses
    /// the authority at its next refresh. Two factors proved: nothing. A first
    /// factor proved: step up. Otherwise (unknown, or an emailed code first):
    /// sign in again. Platform permissions and roles are withheld only when
    /// something is required and <paramref name="enforce"/> is on;
    /// <c>org_perm</c> and the organization claims are always kept.
    /// </summary>
    PlatformMfaDecision Apply(
        TokenClaims platformClaims,
        AuthenticationMethods methods,
        bool hasEnabledSecondFactor,
        bool enforce);

    /// <summary>
    /// The decision for a token about to be minted. An application token
    /// (<paramref name="applicationId"/> not null) is returned untouched, without
    /// reading anything. For a platform token with permissions, reads whether the
    /// account has an enabled second factor and the switch, then applies the
    /// decision; with the switch off it logs what would be required instead.
    /// </summary>
    Task<PlatformMfaDecision> EvaluateAsync(
        Guid userId,
        Guid? applicationId,
        TokenClaims claims,
        AuthenticationMethods methods,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether enforcement is on right now: the switch says so, or the database
    /// settings have not been read since the API started (fail closed). Every
    /// decision below reads it, and so does the refusal of platform grants to
    /// accounts without a factor.
    /// </summary>
    bool IsEnforcing { get; }

    /// <summary>
    /// Whether enforcement applies to an account with these platform claims right
    /// now: it is on (<see cref="IsEnforcing"/>) and the claims carry at least one
    /// permission. The rule that refuses switching the second factor off.
    /// </summary>
    bool IsEnforcedFor(TokenClaims platformClaims);

    /// <summary>
    /// <see cref="IsEnforcedFor"/> for an account: reads its platform claims only
    /// when the switch is on, so with enforcement off it costs nothing.
    /// </summary>
    Task<bool> IsEnforcedForUserAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// What a token about to be minted carries.
/// </summary>
/// <param name="Claims">
/// The claims to mint: the resolved ones, or the same without platform
/// permissions and roles when they are withheld.
/// </param>
/// <param name="Requirement">
/// What the session must prove before the authority returns, in force:
/// <see cref="MfaRequirement.None"/> unless the claims were withheld. Written to
/// <c>mfa_req</c> and to the user info's <c>mfaRequirement</c>.
/// </param>
/// <param name="Assessed">
/// What the rule asks of this session whether or not it is enforced: equal to
/// <paramref name="Requirement"/> under enforcement, and what the readiness log
/// reports while the switch is off.
/// </param>
public sealed record PlatformMfaDecision(
    TokenClaims Claims,
    MfaRequirement Requirement,
    MfaRequirement Assessed)
{
    /// <summary>Gets whether platform permissions and roles were withheld.</summary>
    public bool Withheld => Requirement != MfaRequirement.None;

    /// <summary>The decision that changes nothing.</summary>
    public static PlatformMfaDecision Unchanged(TokenClaims claims) =>
        new(claims, MfaRequirement.None, MfaRequirement.None);
}
