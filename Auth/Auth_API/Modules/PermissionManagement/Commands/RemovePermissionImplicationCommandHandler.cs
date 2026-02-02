using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Handler for removing a permission implication.
/// </summary>
public class RemovePermissionImplicationCommandHandler : IRequestHandler<RemovePermissionImplicationCommand, ErrorOr<bool>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<RemovePermissionImplicationCommandHandler> _logger;

    public RemovePermissionImplicationCommandHandler(
        IPermissionRepository permissionRepository,
        ILogger<RemovePermissionImplicationCommandHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(RemovePermissionImplicationCommand request, CancellationToken cancellationToken)
    {
        // Verify the implication exists
        if (!await _permissionRepository.ImplicationExistsAsync(request.PermissionId, request.ImpliedPermissionId, cancellationToken))
        {
            return PermissionErrors.PermissionNotGranted;
        }

        await _permissionRepository.RemoveImplicationAsync(request.PermissionId, request.ImpliedPermissionId, cancellationToken);

        _logger.LogInformation(
            "Permission implication removed: {PermissionId} no longer implies {ImpliedPermissionId} by {RemovedBy}",
            request.PermissionId, request.ImpliedPermissionId, request.RemovedBy);

        return true;
    }
}
