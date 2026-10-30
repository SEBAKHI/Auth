using System.Security.Claims;
using Auth.Application.Features.Organizations.ApplicationDirectory;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.MachineCredentialChecks;

/// <summary>
/// An application's own server checking a credential presented to it (OI-128 PR B): an API key,
/// a webhook key, or a token. It asks with its machine token, and hears only about its own
/// credentials, as an introspection endpoint answers only the resource server that authenticated
/// to it (RFC 7662 §2.1, §4).
/// </summary>
/// <param name="ApplicationCode">The machine token's <c>client_id</c> (equal to its <c>sub</c>): the application's code.</param>
/// <param name="GrantedScope">The token's <c>scope</c> claim, space-delimited, or null.</param>
public sealed record MachineCredentialCaller(string ApplicationCode, string? GrantedScope)
{
    /// <summary>
    /// Reads the caller from the principal the machine scheme validated. That scheme requires
    /// <c>client_id</c> equal to <c>sub</c> and not shaped like a GUID, so it names an application.
    /// Never <c>aud</c>: it is the one machine audience of every application.
    /// </summary>
    public static MachineCredentialCaller FromPrincipal(ClaimsPrincipal principal) =>
        new(
            principal.FindFirst(JwtClaimNames.ClientId)?.Value ?? string.Empty,
            principal.FindFirst(JwtClaimNames.Scope)?.Value);

    /// <summary>
    /// The calling application, live, when its token was granted <paramref name="requiredScope"/>.
    /// An application that is gone or inactive is refused as a revoked token is
    /// (<see cref="ApplicationDirectoryRefusals.CallerNotServable"/>), and a token without the
    /// scope with a 403 naming it (<see cref="ApplicationDirectoryRefusals.InsufficientScope"/>):
    /// the machine directory's two refusals, answered the same way.
    /// </summary>
    public async Task<ErrorOr<AppEntity>> ResolveAsync(
        IApplicationRepository applications,
        string requiredScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ApplicationCode))
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Missing and soft-deleted rows are both null.
        var application = await applications.GetByCodeAsync(ApplicationCode, cancellationToken);
        if (application is not { IsActive: true })
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Default machine scopes: no consent to read, only the token's grant.
        if (!MachineClients.Scopes.Grants(GrantedScope, requiredScope))
        {
            return ApplicationDirectoryRefusals.InsufficientScope;
        }

        return application;
    }
}
