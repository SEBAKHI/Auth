using Auth.Domain.Errors;
using Auth.Domain.Interfaces.Repositories;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.MachineKeys.MachineAccess;

/// <summary>Reads an application's machine access consents (OI-202).</summary>
public sealed record GetMachineAccessQuery(Guid ApplicationId) : IRequest<ErrorOr<MachineAccessDto>>;

/// <summary>Handles <see cref="GetMachineAccessQuery"/>.</summary>
public sealed class GetMachineAccessQueryHandler : IRequestHandler<GetMachineAccessQuery, ErrorOr<MachineAccessDto>>
{
    private readonly IApplicationRepository _applications;

    public GetMachineAccessQueryHandler(IApplicationRepository applications)
    {
        _applications = applications;
    }

    public async Task<ErrorOr<MachineAccessDto>> Handle(GetMachineAccessQuery request, CancellationToken cancellationToken)
    {
        var application = await _applications.GetByIdAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            return ApplicationErrors.NotFound(request.ApplicationId);
        }

        return new MachineAccessDto { OrganizationsReadAll = application.AllowMachineOrganizationsRead };
    }
}
