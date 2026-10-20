using System.Security.Claims;
using Auth.Domain.Constants;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// Who reads an organization's directory through an application (OI-194): the user the
/// application's access token names, the application it was issued to, and the scopes it was
/// granted. Only who is asking: what they may read is decided by
/// <see cref="ApplicationDirectoryAccess"/> from the database, never from the token's
/// <c>permissions</c> or <c>org_perm</c>, which it does not carry.
/// </summary>
/// <param name="UserId">The token's <c>sub</c>; <see cref="Guid.Empty"/> when it is not a user id.</param>
/// <param name="ApplicationCode">The token's single audience: the application's code.</param>
/// <param name="GrantedScope">The token's <c>scope</c> claim, space-delimited, or null.</param>
public sealed record ApplicationDirectoryCaller(Guid UserId, string ApplicationCode, string? GrantedScope)
{
    /// <summary>
    /// Reads the caller from the principal the application-token scheme validated: that scheme
    /// accepts exactly one audience, shaped like an application code, so <c>aud</c> is it.
    /// </summary>
    public static ApplicationDirectoryCaller FromPrincipal(ClaimsPrincipal principal) =>
        new(
            Guid.TryParse(principal.FindFirst(JwtClaimNames.Subject)?.Value, out var userId) ? userId : Guid.Empty,
            principal.FindFirst(JwtClaimNames.Audience)?.Value ?? string.Empty,
            principal.FindFirst(JwtClaimNames.Scope)?.Value);
}
