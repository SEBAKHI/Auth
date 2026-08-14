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
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<SetApplicationActiveCommandHandler> _logger;

    public SetApplicationActiveCommandHandler(
        IApplicationRepository applicationRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUserSessionRepository sessionRepository,
        IPublisher publisher,
        ILogger<SetApplicationActiveCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _sessionRepository = sessionRepository;
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
            // Nothing to do, and nothing to revoke. Reported as success so a
            // double-click on the switch is not an error the operator has to
            // interpret.
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
            // inactive application already; revoking here closes the gap between
            // "cannot get a new token" and "the token you hold stops working".
            //
            // Residual window, stated plainly: an access token minted moments
            // before this runs stays valid until it expires on its own
            // (Jwt:AccessTokenLifetime). Closing that would need per-application
            // access-token blacklisting, which is a separate piece of work.
            await _refreshTokenRepository.RevokeAllForApplicationAsync(
                application.Id,
                request.ModifiedBy,
                TokenRevocationReasons.ApplicationDeactivated,
                cancellationToken);

            await _sessionRepository.TerminateForApplicationAsync(
                application.Id,
                TokenRevocationReasons.ApplicationDeactivated,
                cancellationToken);
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
}
