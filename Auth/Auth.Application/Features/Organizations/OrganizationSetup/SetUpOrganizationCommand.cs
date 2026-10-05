using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.OrganizationSetup;

/// <summary>
/// The organization-creation step for an application: creates the signed-in
/// user's organization (<see cref="Name"/>) or sets up one they already own
/// (<see cref="OrganizationId"/>), enables the application for it and grants
/// the application's creator role there, in one transaction.
/// </summary>
/// <param name="IdpSessionToken">The plain IdP session cookie value, if present.</param>
/// <param name="ClientId">The application code, from the page's returnTo.</param>
/// <param name="Name">The new organization's name; ignored when <paramref name="OrganizationId"/> is set.</param>
/// <param name="OrganizationId">An organization the user owns, to set up instead of creating one.</param>
public record SetUpOrganizationCommand(
    string? IdpSessionToken,
    string? ClientId,
    string? Name,
    Guid? OrganizationId) : IRequest<ErrorOr<SetUpOrganizationResult>>;

/// <summary>
/// The organization now set up for the application, or that the browser has no
/// usable single sign-on session (answered 401 by the API layer).
/// </summary>
public sealed record SetUpOrganizationResult(bool SignInRequired, Guid? OrganizationId)
{
    public static readonly SetUpOrganizationResult SignIn = new(true, null);
}
