using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.MachineKeys.MachineAccess;

/// <summary>
/// Switches an application's consent to the machine scope <c>organizations.read.all</c> (OI-202):
/// whether its server, with its machine key and no user, may read the directory of every
/// organization that enabled it. Switching it off takes effect at the next read; a new token
/// request for the scope is then refused.
/// </summary>
/// <param name="ApplicationId">The application.</param>
/// <param name="Allowed">The consent's new value.</param>
public sealed record SetMachineOrganizationsReadCommand(Guid ApplicationId, bool Allowed) : IRequest<ErrorOr<MachineAccessDto>>
{
    /// <summary>The administrator changing the consent.</summary>
    public Guid ChangedBy { get; init; }
}

/// <summary>
/// Handles <see cref="SetMachineOrganizationsReadCommand"/>. Saving the value the consent already
/// has writes nothing and raises no event.
/// </summary>
public sealed class SetMachineOrganizationsReadCommandHandler
    : IRequestHandler<SetMachineOrganizationsReadCommand, ErrorOr<MachineAccessDto>>
{
    private readonly IApplicationRepository _applications;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly ILogger<SetMachineOrganizationsReadCommandHandler> _logger;

    public SetMachineOrganizationsReadCommandHandler(
        IApplicationRepository applications,
        IPublisher publisher,
        TimeProvider time,
        ILogger<SetMachineOrganizationsReadCommandHandler> logger)
    {
        _applications = applications;
        _publisher = publisher;
        _time = time;
        _logger = logger;
    }

    public async Task<ErrorOr<MachineAccessDto>> Handle(
        SetMachineOrganizationsReadCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        if (application.SetMachineOrganizationsRead(request.Allowed, request.ChangedBy))
        {
            if (!await _applications.UpdateMachineOrganizationsReadAsync(application, cancellationToken))
            {
                return ApplicationErrors.NotFound(request.ApplicationId);
            }

            _logger.LogInformation(
                "Machine directory read of application {ApplicationId} switched {State} by {ChangedBy}",
                application.Id, request.Allowed ? "on" : "off", request.ChangedBy);

            await _publisher.Publish(
                new MachineOrganizationsReadChangedEvent(
                    Guid.NewGuid(), application.Id, request.Allowed, request.ChangedBy, _time.GetUtcNow()),
                CancellationToken.None);
        }

        return new MachineAccessDto { OrganizationsReadAll = application.AllowMachineOrganizationsRead };
    }
}
