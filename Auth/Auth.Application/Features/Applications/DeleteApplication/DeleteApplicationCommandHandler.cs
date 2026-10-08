using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.Errors;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Applications.DeleteApplication;

/// <summary>
/// Handler for deleting an application.
/// </summary>
public class DeleteApplicationCommandHandler : IRequestHandler<DeleteApplicationCommand, ErrorOr<bool>>
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly ICredentialRevocationService _credentialRevocation;
    private readonly ILogger<DeleteApplicationCommandHandler> _logger;

    public DeleteApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        ICredentialRevocationService credentialRevocation,
        ILogger<DeleteApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
        _credentialRevocation = credentialRevocation;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(DeleteApplicationCommand request, CancellationToken cancellationToken)
    {
        var application = await _applicationRepository.GetByIdAsync(request.Id, cancellationToken);

        if (application == null)
        {
            return ApplicationErrors.NotFound(request.Id);
        }

        // People and tenants must be detached deliberately before deletion;
        // credentials (API/webhook keys) are owned by the application and are
        // revoked with it inside the soft-delete transaction.
        if (await _applicationRepository.HasActiveUserAssignmentsAsync(request.Id, cancellationToken))
        {
            return ApplicationErrors.HasActiveUsers;
        }

        if (await _applicationRepository.HasActiveOrganizationsAsync(request.Id, cancellationToken))
        {
            return ApplicationErrors.HasActiveOrganizations;
        }

        // The tokens go BEFORE the row: a deleted application can no longer be
        // looked up, so a revocation that failed after the delete could never be
        // retried. In this order a failure leaves the application in place and
        // the operator simply deletes again. Every session of the application
        // ends and each id is blacklisted, so the access tokens already out are
        // refused from the next request. From this first write on, the caller
        // cannot call it off.
        await _credentialRevocation.TerminateApplicationSessionsAsync(
            application.Id,
            userId: null,
            request.DeletedBy,
            TokenRevocationReasons.ApplicationDeleted,
            CancellationToken.None);

        await _applicationRepository.DeleteAsync(request.Id, request.DeletedBy, CancellationToken.None);

        _logger.LogInformation(
            "Application deleted: {ApplicationId} ({ApplicationCode}) by {DeletedBy}",
            request.Id, application.Code, request.DeletedBy);

        return true;
    }
}
