using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Commands;

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

        // Check if application is a system application (AUTH application cannot be deleted)
        if (application.Code == "AUTH")
        {
            return ApplicationErrors.CannotDeleteSystemApplication;
        }

        // Check if application has active API keys
        if (await _applicationRepository.HasActiveApiKeysAsync(request.Id, cancellationToken))
        {
            return ApplicationErrors.HasActiveApiKeys;
        }

        // Check if application has active user assignments
        if (await _applicationRepository.HasActiveUserAssignmentsAsync(request.Id, cancellationToken))
        {
            return ApplicationErrors.HasActiveUsers;
        }

        // Check if application has active organizations
        if (await _applicationRepository.HasActiveOrganizationsAsync(request.Id, cancellationToken))
        {
            return ApplicationErrors.HasActiveOrganizations;
        }

        await _applicationRepository.DeleteAsync(request.Id, cancellationToken);

        _logger.LogInformation(
            "Application deleted: {ApplicationId} ({ApplicationCode}) by {DeletedBy}",
            request.Id, application.Code, request.DeletedBy);

        return true;
    }
}
