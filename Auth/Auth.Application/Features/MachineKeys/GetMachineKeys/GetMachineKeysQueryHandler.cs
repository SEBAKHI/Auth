using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.MachineKeys.GetMachineKeys;

/// <summary>
/// Handles <see cref="GetMachineKeysQuery"/>. The answer carries no key material of any kind.
/// </summary>
public class GetMachineKeysQueryHandler : IRequestHandler<GetMachineKeysQuery, ErrorOr<IReadOnlyList<MachineKeyDto>>>
{
    private readonly IApplicationRepository _applications;
    private readonly IApplicationMachineKeyRepository _machineKeys;

    public GetMachineKeysQueryHandler(
        IApplicationRepository applications,
        IApplicationMachineKeyRepository machineKeys)
    {
        _applications = applications;
        _machineKeys = machineKeys;
    }

    public async Task<ErrorOr<IReadOnlyList<MachineKeyDto>>> Handle(
        GetMachineKeysQuery request,
        CancellationToken cancellationToken)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        var keys = await _machineKeys.ListByApplicationAsync(request.ApplicationId, cancellationToken);
        return keys.Select(ToDto).ToList();
    }

    internal static MachineKeyDto ToDto(ApplicationMachineKey key) => new()
    {
        Id = key.Id,
        ApplicationId = key.ApplicationId,
        Kid = key.Kid,
        Name = key.Name,
        Algorithm = key.Algorithm,
        Purpose = key.Purpose,
        CreatedAt = key.CreatedAt,
        CreatedBy = key.CreatedBy,
        LastUsedAt = key.LastUsedAt,
        IsRevoked = key.IsRevoked,
        RevokedAt = key.RevokedAt,
        RevokeReason = key.RevokeReason,
    };
}
