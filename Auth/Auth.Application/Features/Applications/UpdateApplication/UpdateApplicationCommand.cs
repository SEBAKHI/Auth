using Auth.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.UpdateApplication;

/// <summary>
/// Command to update an existing application.
/// </summary>
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
    int? ReauthenticationMaxAgeMinutes = null) : IRequest<ErrorOr<ApplicationDto>>
{
    /// <summary>
    /// The ID of the user modifying this application (for audit).
    /// </summary>
    public Guid ModifiedBy { get; init; }
}
