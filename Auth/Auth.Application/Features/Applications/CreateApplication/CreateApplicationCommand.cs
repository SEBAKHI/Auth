using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.CreateApplication;

/// <summary>
/// Command to create a new application.
/// </summary>
/// <param name="AllowedScopes">
/// The OAuth scopes the application may be granted beyond <c>openid</c>. Null or
/// absent means none (openid only); <c>openid</c> may be named and changes nothing.
/// </param>
/// <param name="AllowOrganizationCreation">
/// Accepted for symmetry with update and always refused when true: the creator
/// role must be one of this application's roles, and none exists before the
/// application does. Switch it on with an update.
/// </param>
/// <param name="OrganizationCreatorRoleId">Refused for the same reason when set.</param>
public record CreateApplicationCommand(
    string Code,
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
    /// The ID of the user creating this application (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
