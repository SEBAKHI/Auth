using Auth_Lib.Domain.Entities;
using Auth_Lib.Domain.Interfaces.Repositories;
using Auth_Lib.Errors;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.PermissionManagement.Commands;

/// <summary>
/// Handler for adding a permission implication.
/// </summary>
public class AddPermissionImplicationCommandHandler : IRequestHandler<AddPermissionImplicationCommand, ErrorOr<bool>>
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly ILogger<AddPermissionImplicationCommandHandler> _logger;

    public AddPermissionImplicationCommandHandler(
        IPermissionRepository permissionRepository,
        ILogger<AddPermissionImplicationCommandHandler> logger)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
    }

    public async Task<ErrorOr<bool>> Handle(AddPermissionImplicationCommand request, CancellationToken cancellationToken)
    {
        // Verify both permissions exist
        var permission = await _permissionRepository.GetByIdAsync(request.PermissionId, cancellationToken);
        if (permission == null)
        {
            return PermissionErrors.NotFound(request.PermissionId);
        }

        var impliedPermission = await _permissionRepository.GetByIdAsync(request.ImpliedPermissionId, cancellationToken);
        if (impliedPermission == null)
        {
            return PermissionErrors.NotFound(request.ImpliedPermissionId);
        }

        // Check if implication already exists
        if (await _permissionRepository.ImplicationExistsAsync(request.PermissionId, request.ImpliedPermissionId, cancellationToken))
        {
            return PermissionErrors.PermissionAlreadyGranted;
        }

        // Check for circular implications
        if (await _permissionRepository.WouldCreateCircularImplicationAsync(request.PermissionId, request.ImpliedPermissionId, cancellationToken))
        {
            return PermissionErrors.CircularImplication;
        }

        var implication = PermissionImplication.Create(request.PermissionId, request.ImpliedPermissionId, request.CreatedBy);
        await _permissionRepository.AddImplicationAsync(implication, cancellationToken);

        _logger.LogInformation(
            "Permission implication added: {PermissionId} ({PermissionCode}) implies {ImpliedPermissionId} ({ImpliedCode}) by {CreatedBy}",
            request.PermissionId, permission.Code, request.ImpliedPermissionId, impliedPermission.Code, request.CreatedBy);

        return true;
    }
}
