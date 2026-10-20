using System.Security.Claims;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.ValueObjects;
using ErrorOr;
using AppEntity = Auth.Domain.Entities.Application;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// Who reads an organization's directory: a user through an application
/// (<see cref="ApplicationDirectoryCaller"/>, OI-194), or an application by itself, with its machine
/// key and no user (<see cref="ApplicationMachineCaller"/>, OI-202). Only who is asking, and the two
/// steps that differ by who it is: what the caller may read is decided by
/// <see cref="ApplicationDirectoryAccess"/> from the database, never from the token's
/// <c>permissions</c> or <c>org_perm</c>, which it does not carry.
/// </summary>
/// <param name="ApplicationCode">The application's code, as the token names it.</param>
/// <param name="GrantedScope">The token's <c>scope</c> claim, space-delimited, or null.</param>
public abstract record ApplicationDirectoryReader(string ApplicationCode, string? GrantedScope)
{
    /// <summary>
    /// The calling application, active, and still allowed the scope the read requires; or
    /// <see cref="ApplicationDirectoryRefusals.CallerNotServable"/> or
    /// <see cref="ApplicationDirectoryRefusals.InsufficientScope"/>.
    /// </summary>
    internal abstract Task<ErrorOr<AppEntity>> ResolveApplicationAsync(
        ApplicationDirectorySources sources,
        CancellationToken cancellationToken);

    /// <summary>
    /// What this caller may read in <paramref name="organization"/>, which
    /// <see cref="ApplicationDirectoryAccess"/> already found active and enabled for the
    /// application; or <see cref="OrganizationErrors.NotFound"/>.
    /// </summary>
    internal abstract Task<ErrorOr<ApplicationDirectoryAuthority>> AuthorityInAsync(
        ApplicationDirectorySources sources,
        Organization organization,
        CancellationToken cancellationToken);

    /// <summary>The application named <see cref="ApplicationCode"/>, when it exists and is active.</summary>
    private protected async Task<AppEntity?> ActiveApplicationAsync(
        ApplicationDirectorySources sources,
        CancellationToken cancellationToken)
    {
        var application = await sources.Applications.GetByCodeAsync(ApplicationCode, cancellationToken);
        return application is { IsActive: true } ? application : null;
    }
}

/// <summary>
/// A user reading an organization's directory through an application (OI-194): the user the
/// application's access token names, the application it was issued to, and the scopes it was
/// granted.
/// </summary>
/// <param name="UserId">The token's <c>sub</c>; <see cref="Guid.Empty"/> when it is not a user id.</param>
/// <param name="ApplicationCode">The token's single audience: the application's code.</param>
/// <param name="GrantedScope">The token's <c>scope</c> claim, space-delimited, or null.</param>
public sealed record ApplicationDirectoryCaller(Guid UserId, string ApplicationCode, string? GrantedScope)
    : ApplicationDirectoryReader(ApplicationCode, GrantedScope)
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

    /// <inheritdoc />
    internal override async Task<ErrorOr<AppEntity>> ResolveApplicationAsync(
        ApplicationDirectorySources sources,
        CancellationToken cancellationToken)
    {
        if (UserId == Guid.Empty || string.IsNullOrEmpty(ApplicationCode))
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Missing and soft-deleted rows are both null. The rule a refresh applies: an account
        // that may not renew credentials is not served by the token it still holds either.
        var user = await sources.Users.GetByIdAsync(UserId, cancellationToken);
        if (user is null || !user.CanRenewCredentials())
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        var application = await ActiveApplicationAsync(sources, cancellationToken);
        if (application is null)
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Granted to the token AND still allowed to the application: an administrator's removal
        // takes effect at the next call, not at the token's next refresh.
        if (!ScopeSet.FromStored(GrantedScope).Contains(OAuthScopes.OrganizationsRead)
            || !application.AllowedScopes.Contains(OAuthScopes.OrganizationsRead))
        {
            return ApplicationDirectoryRefusals.InsufficientScope;
        }

        return application;
    }

    /// <inheritdoc />
    internal override async Task<ErrorOr<ApplicationDirectoryAuthority>> AuthorityInAsync(
        ApplicationDirectorySources sources,
        Organization organization,
        CancellationToken cancellationToken)
    {
        var membership = await sources.Organizations.GetMembershipAsync(organization.Id, UserId, cancellationToken);
        if (membership is null || !membership.IsValid())
        {
            return OrganizationErrors.NotFound(organization.Id);
        }

        // The same query the permission gate falls back to: the membership role's org: codes,
        // live. Matched by the gate's wildcard rule (PermissionCode.Matches).
        var codes = await sources.Organizations.GetMembershipPermissionCodesAsync(organization.Id, UserId, cancellationToken);
        return new ApplicationDirectoryMembershipAuthority(UserId, codes);
    }
}

/// <summary>
/// An application reading an organization's directory by itself, with its machine key and no user
/// (OI-202): the application the machine token was issued to, and the scopes it was granted. No
/// person stands behind it, so it has no membership to test: its authority is fixed
/// (<see cref="ApplicationDirectoryMachineAuthority"/>).
/// </summary>
/// <param name="ApplicationCode">The token's <c>client_id</c> (equal to its <c>sub</c>): the application's code.</param>
/// <param name="GrantedScope">The token's <c>scope</c> claim, space-delimited, or null.</param>
public sealed record ApplicationMachineCaller(string ApplicationCode, string? GrantedScope)
    : ApplicationDirectoryReader(ApplicationCode, GrantedScope)
{
    /// <summary>
    /// Reads the caller from the principal the machine scheme validated. That scheme requires
    /// <c>client_id</c> equal to <c>sub</c> and not shaped like a GUID, so it names an application,
    /// never a user. The token's audience is the one machine audience of every application and
    /// says nothing about which one is asking: it is not read.
    /// </summary>
    public static ApplicationMachineCaller FromPrincipal(ClaimsPrincipal principal) =>
        new(
            principal.FindFirst(JwtClaimNames.ClientId)?.Value ?? string.Empty,
            principal.FindFirst(JwtClaimNames.Scope)?.Value);

    /// <inheritdoc />
    internal override async Task<ErrorOr<AppEntity>> ResolveApplicationAsync(
        ApplicationDirectorySources sources,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ApplicationCode))
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        var application = await ActiveApplicationAsync(sources, cancellationToken);
        if (application is null)
        {
            return ApplicationDirectoryRefusals.CallerNotServable;
        }

        // Granted to the token AND consented to the application now: switching the consent off
        // takes effect at the next call, whatever the token still says.
        if (!MachineClients.Scopes.Grants(GrantedScope, MachineClients.Scopes.OrganizationsReadAll)
            || !application.ConsentedMachineScopes.Contains(MachineClients.Scopes.OrganizationsReadAll, StringComparer.Ordinal))
        {
            return ApplicationDirectoryRefusals.InsufficientScope;
        }

        return application;
    }

    /// <inheritdoc />
    internal override Task<ErrorOr<ApplicationDirectoryAuthority>> AuthorityInAsync(
        ApplicationDirectorySources sources,
        Organization organization,
        CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<ApplicationDirectoryAuthority>>(ApplicationDirectoryMachineAuthority.Instance);
}
