using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.SetApplicationActive;

/// <summary>
/// Handler for switching an application on or off.
/// </summary>
public class SetApplicationActiveCommandHandler : IRequestHandler<SetApplicationActiveCommand, ErrorOr<Success>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly IPublisher _publisher;
    private readonly ILogger<SetApplicationActiveCommandHandler> _logger;

    public SetApplicationActiveCommandHandler(
        IApplicationRepository applicationRepository,
        ICredentialRevocationService credentialRevocation,
        IPublisher publisher,
        ILogger<SetApplicationActiveCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _credentialRevocation = credentialRevocation;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        SetApplicationActiveCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.Id, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.Id);
        }

        if (application.IsActive == request.IsActive)
        {
            // Reported as success so a double-click on the switch is not an error
            // the operator has to interpret. Switching ON what is on does nothing.
            // Switching OFF what is off runs the revocation again: it is safe to
            // repeat, and it is how a retry completes a switch-off whose revocation
            // failed after the application was saved. Nothing is published again.
            if (!request.IsActive)
            {
                await RevokeApplicationSessionsAsync(application.Id, request.ModifiedBy, cancellationToken);
            }

            return Result.Success;
        }

        if (request.IsActive)
        {
            application.Activate(request.ModifiedBy);
        }
        else
        {
            application.Deactivate(request.ModifiedBy);
        }

        await _applicationRepository.UpdateAsync(application, cancellationToken);

        if (!request.IsActive)
        {
            // The authorize, token-exchange and refresh paths all reject an
            // inactive application already. This makes the tokens already out
            // stop too: every session of the application ends and its id is
            // blacklisted, so its access tokens are refused from the next request.
            //
            // Residual window, stated plainly: a token whose session row was never
            // written (that insert is best-effort), or one minted in the same
            // instant as this runs, stays valid until it expires on its own
            // (Jwt:AccessTokenLifetime).
            await RevokeApplicationSessionsAsync(application.Id, request.ModifiedBy, cancellationToken);
        }

        _logger.LogInformation(
            "Application {ApplicationId} ({ApplicationCode}) switched {State} by {ModifiedBy}",
            application.Id, application.Code, request.IsActive ? "on" : "off", request.ModifiedBy);

        await _publisher.Publish(
            new ApplicationActivationChangedEvent(
                application.Id, application.Code, request.IsActive, request.ModifiedBy),
            cancellationToken);

        return Result.Success;
    }

    private Task<int> RevokeApplicationSessionsAsync(
        Guid applicationId,
        Guid modifiedBy,
        CancellationToken cancellationToken) =>
        _credentialRevocation.TerminateApplicationSessionsAsync(
            applicationId,
            userId: null,
            modifiedBy,
            TokenRevocationReasons.ApplicationDeactivated,
            cancellationToken);
}
