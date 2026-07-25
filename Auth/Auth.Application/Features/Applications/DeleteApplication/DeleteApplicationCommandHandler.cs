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
    private readonly ILogger<DeleteApplicationCommandHandler> _logger;

    public DeleteApplicationCommandHandler(
        IApplicationRepository applicationRepository,
        ILogger<DeleteApplicationCommandHandler> logger)
    {
        _applicationRepository = applicationRepository;
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

        await _applicationRepository.DeleteAsync(request.Id, request.DeletedBy, cancellationToken);

        _logger.LogInformation(
            "Application deleted: {ApplicationId} ({ApplicationCode}) by {DeletedBy}",
            request.Id, application.Code, request.DeletedBy);

        return true;
    }
}
