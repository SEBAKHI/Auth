using Auth.Application.DTOs;
using Auth.Domain.Enums;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.CreateApplication;

/// <summary>
/// Command to create a new application.
/// </summary>
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
    ApplicationAccessMode AccessMode = ApplicationAccessMode.Restricted) : IRequest<ErrorOr<ApplicationDto>>
{
    /// <summary>
    /// The ID of the user creating this application (for audit).
    /// </summary>
    public Guid CreatedBy { get; init; }
}
