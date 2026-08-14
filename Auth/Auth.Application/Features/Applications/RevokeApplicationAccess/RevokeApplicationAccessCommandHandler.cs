using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.RevokeApplicationAccess;

/// <summary>
/// Handler for withdrawing a user's invitation to an application.
/// </summary>
public class RevokeApplicationAccessCommandHandler : IRequestHandler<RevokeApplicationAccessCommand, ErrorOr<Success>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IApplicationAccessRepository _accessRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserSessionRepository _sessionRepository;
    private readonly IPublisher _publisher;
    private readonly ILogger<RevokeApplicationAccessCommandHandler> _logger;

    public RevokeApplicationAccessCommandHandler(
        IApplicationRepository applicationRepository,
        IApplicationAccessRepository accessRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUserSessionRepository sessionRepository,
        IPublisher publisher,
        ILogger<RevokeApplicationAccessCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _accessRepository = accessRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _sessionRepository = sessionRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ErrorOr<Success>> Handle(
        RevokeApplicationAccessCommand request,
        CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var grant = await _accessRepository.GetGrantAsync(
            request.ApplicationId, request.UserId, cancellationToken);

        if (grant is null || !grant.IsValid())
        {
            return ApplicationErrors.UserAccessNotFound(request.UserId);
        }

        grant.Revoke(request.RevokedBy);
        await _accessRepository.UpdateGrantAsync(grant, cancellationToken);

        // This user, this application, and nothing else: losing access to one
        // application must not sign them out of the others. Their already-issued
        // access token for this application survives until it expires on its own.
        await _refreshTokenRepository.RevokeForUserAndApplicationAsync(
            request.UserId,
            request.ApplicationId,
            request.RevokedBy,
            TokenRevocationReasons.ApplicationAccessRevoked,
            cancellationToken);

        await _sessionRepository.TerminateForUserAndApplicationAsync(
            request.UserId,
            request.ApplicationId,
            TokenRevocationReasons.ApplicationAccessRevoked,
            cancellationToken);

        _logger.LogInformation(
            "Access to application {ApplicationId} ({ApplicationCode}) revoked for user {UserId} by {RevokedBy}",
            application.Id, application.Code, request.UserId, request.RevokedBy);

        await _publisher.Publish(
            new ApplicationAccessRevokedEvent(
                application.Id, application.Code, request.UserId, request.RevokedBy),
            cancellationToken);

        return Result.Success;
    }
}
