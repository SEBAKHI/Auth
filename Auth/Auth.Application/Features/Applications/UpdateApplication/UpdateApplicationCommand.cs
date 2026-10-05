using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplication;

/// <summary>
/// Command to update an existing application.
/// </summary>
/// <param name="AllowedScopes">
/// The OAuth scopes the application may be granted beyond <c>openid</c>. Null
/// leaves them UNCHANGED, like <c>RedirectUris</c>: a client built before the
/// field existed sends none, and must not strip an application's scopes by
/// renaming it. An empty list clears them to <c>openid</c> only.
/// </param>
/// <param name="AllowOrganizationCreation">
/// Whether the application may ask for the user's organization to be created.
/// Null leaves it UNCHANGED (the logo upload re-sends the body without it).
/// </param>
/// <param name="OrganizationCreatorRoleId">
/// The application role granted inside an organization the step creates or
/// sets up. Null leaves it UNCHANGED; the empty GUID clears it.
/// </param>
public record UpdateApplicationCommand(
    Guid Id,
    string Name,
    string? Description = null,
    string? BaseUrl = null,
    string? LogoUrl = null,
    string? ContactEmail = null,
    bool AllowSelfRegistration = false,
    bool RequireTwoFactor = false,
    bool RequireEmailVerification = false,
    int SessionTimeoutMinutes = 60,
    int MaxConcurrentSessions = 5,
    IReadOnlyList<string>? RedirectUris = null,
    int? ReauthenticationMaxAgeMinutes = null,
    ApplicationAccessMode AccessMode = ApplicationAccessMode.Restricted,
    IReadOnlyList<string>? AllowedScopes = null,
    bool? AllowOrganizationCreation = null,
    Guid? OrganizationCreatorRoleId = null) : IRequest<ErrorOr<ApplicationDto>>
{
    /// <summary>
    /// The ID of the user modifying this application (for audit).
    /// </summary>
    public Guid ModifiedBy { get; init; }
}
