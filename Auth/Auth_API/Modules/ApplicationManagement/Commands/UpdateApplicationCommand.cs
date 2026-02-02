using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Commands;

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
    int MaxConcurrentSessions = 5) : IRequest<ErrorOr<ApplicationDto>>
{
    /// <summary>
    /// The ID of the user modifying this application (for audit).
    /// </summary>
    public Guid ModifiedBy { get; set; }
}
