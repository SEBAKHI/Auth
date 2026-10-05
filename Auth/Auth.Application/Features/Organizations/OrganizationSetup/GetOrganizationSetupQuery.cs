using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// What the organization-creation page may offer the signed-in user for one
/// application: creating an organization, using one they already own, or both.
/// </summary>
/// <param name="IdpSessionToken">The plain IdP session cookie value, if present.</param>
/// <param name="ClientId">The application code, from the page's returnTo.</param>
public record GetOrganizationSetupQuery(
    string? IdpSessionToken,
    string? ClientId) : IRequest<ErrorOr<OrganizationSetupStateResult>>;

/// <summary>
/// The page's state, or that the browser has no usable single sign-on session.
/// </summary>
/// <remarks>
/// "Sign in first" is a 401 with the transport code, which no catalog error can
/// carry; the handler decides and the API layer answers, as it does for the
/// authorize endpoint's cookies.
/// </remarks>
public sealed record OrganizationSetupStateResult(bool SignInRequired, OrganizationSetupState? State)
{
    public static readonly OrganizationSetupStateResult SignIn = new(true, null);
}

/// <param name="CanCreate">Whether the user may create one more self-service organization.</param>
/// <param name="Limit">How many self-service organizations a user may own.</param>
/// <param name="OwnedOrganizations">Organizations the user owns that could be set up instead.</param>
/// <param name="Email">
/// The address of the account the single sign-on session belongs to: the one
/// the organization is created for. The page shows it, and not the accounts
/// app's own session, which may belong to someone else.
/// </param>
public sealed record OrganizationSetupState(
    bool CanCreate,
    int Limit,
    IReadOnlyList<OrganizationSetupOption> OwnedOrganizations,
    string Email);

/// <summary>An organization the user may set up for the application.</summary>
public sealed record OrganizationSetupOption(Guid Id, string Name);
