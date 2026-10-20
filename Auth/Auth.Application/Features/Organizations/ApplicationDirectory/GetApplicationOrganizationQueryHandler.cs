using Auth.Application.Common;
using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>Handles <see cref="GetApplicationOrganizationQuery"/>.</summary>
public sealed class GetApplicationOrganizationQueryHandler
    : IRequestHandler<GetApplicationOrganizationQuery, ErrorOr<ApplicationOrganizationResponse>>
{
    private readonly ApplicationDirectoryAccess _access;
    private readonly IImageUrlComposer _imageUrlComposer;
    private readonly ILogger<GetApplicationOrganizationQueryHandler> _logger;

    public GetApplicationOrganizationQueryHandler(
        ApplicationDirectoryAccess access,
        IImageUrlComposer imageUrlComposer,
        ILogger<GetApplicationOrganizationQueryHandler> logger)
    {
        _access = access;
        _imageUrlComposer = imageUrlComposer;
        _logger = logger;
    }

    public async Task<ErrorOr<ApplicationOrganizationResponse>> Handle(
        GetApplicationOrganizationQuery request,
        CancellationToken cancellationToken)
    {
        var grant = await _access.ResolveAsync(request.Caller, request.OrganizationId, requiredPermission: null, cancellationToken);
        if (grant.IsError)
        {
            return grant.Errors;
        }

        var organization = grant.Value.Organization;
        ApplicationDirectoryLog.Read(_logger, grant.Value, "organization", itemCount: 1);

        return new ApplicationOrganizationResponse
        {
            Id = organization.Id,
            Name = organization.Name,
            LogoUrl = _imageUrlComposer.ComposeAbsolute(organization.LogoUrl)
        };
    }
}
