namespace Auth.Application.Interfaces;

/// <summary>
/// The authority claims that go into an access token.
/// </summary>
public sealed record TokenClaims(
    IReadOnlyList<string> RoleCodes,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<(Guid OrganizationId, string Code)> OrganizationPermissions);

/// <summary>
/// Resolves the claims an access token should carry, scoped to the application
/// the token is for.
/// </summary>
/// <remarks>
/// One place, used by both the mint and the refresh paths, because the bug this
/// replaces was exactly the two paths independently reading the unscoped
/// queries: a role granted only for application A rode along in a token whose
/// audience was application B, and B enforced permissions it had never issued.
/// </remarks>
public interface ITokenClaimsResolver
{
    /// <summary>
    /// Resolves the claims for one user.
    /// </summary>
    /// <param name="applicationId">
    /// The application the token is scoped to, or null for a platform token.
    /// Non-null narrows every claim to that application: roles assigned with
    /// that scope or held through an organization that has it enabled,
    /// permissions from those roles and from application-scoped direct grants,
    /// and organization claims only for organizations where it is enabled.
    /// </param>
    Task<TokenClaims> ResolveAsync(
        Guid userId,
        Guid? applicationId,
        CancellationToken cancellationToken);
}
