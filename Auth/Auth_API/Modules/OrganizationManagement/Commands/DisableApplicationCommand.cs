using ErrorOr;
using MediatR;

namespace Auth_API.Modules.OrganizationManagement.Commands;

/// <summary>
/// Command to disable an application for an organization.
/// </summary>
public record DisableApplicationCommand(
    Guid OrganizationId,
    Guid ApplicationId) : IRequest<ErrorOr<bool>>
{
    /// <summary>
    /// The user disabling the application.
    /// </summary>
    public Guid DisabledBy { get; init; }
}
