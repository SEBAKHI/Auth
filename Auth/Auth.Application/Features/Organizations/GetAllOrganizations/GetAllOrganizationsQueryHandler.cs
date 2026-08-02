using Auth.Domain.Interfaces.Repositories;
using Auth.Application.Common;
using Auth.Application.DTOs;
using Auth.Application.Interfaces;
using ErrorOr;
using MediatR;

namespace Auth.Application.Features.Organizations.GetAllOrganizations;

/// <summary>
/// Handler for getting a paginated list of ALL organizations (platform
/// administration). Member/application counts and owner info are resolved
/// with batched lookups — no per-row queries.
/// </summary>
public class GetAllOrganizationsQueryHandler
    : IRequestHandler<GetAllOrganizationsQuery, ErrorOr<PagedOrganizationsDto>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IImageUrlComposer _imageUrlComposer;

    public GetAllOrganizationsQueryHandler(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IImageUrlComposer imageUrlComposer)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _imageUrlComposer = imageUrlComposer;
    }

    public async Task<ErrorOr<PagedOrganizationsDto>> Handle(
        GetAllOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var (organizations, totalCount) = await _organizationRepository.GetPagedAsync(
            request.PageNumber,
            request.PageSize,
            request.SearchTerm,
            request.SortBy,
            request.SortDirection,
            cancellationToken);

        var organizationIds = organizations.Select(o => o.Id).ToList();
        var memberCounts = await _organizationRepository.GetMemberCountsAsync(
            organizationIds, cancellationToken);
        var appCounts = await _organizationRepository.GetEnabledApplicationCountsAsync(
            organizationIds, cancellationToken);

        var owners = (await _userRepository.GetByIdsAsync(
                organizations.Select(o => o.OwnerId).Distinct().ToList(), cancellationToken))
            .ToDictionary(u => u.Id);
        var auditNames = await NameLookupHelper.UserNamesAsync(
            _userRepository,
            organizations.SelectMany(o => new Guid?[] { o.CreatedBy, o.ModifiedBy }),
            cancellationToken);

        var organizationDtos = organizations.Select(organization =>
        {
            var owner = owners.GetValueOrDefault(organization.OwnerId);
            return new OrganizationDto
            {
                Id = organization.Id,
                Code = organization.Code,
                Name = organization.Name,
                Description = organization.Description,
                LogoUrl = _imageUrlComposer.Compose(organization.LogoUrl),
                Website = organization.Website,
                ContactEmail = organization.ContactEmail,
                OwnerId = organization.OwnerId,
                OwnerName = owner != null ? $"{owner.FirstName} {owner.LastName}".Trim() : null,
                OwnerEmail = owner?.Email?.Value,
                IsActive = organization.IsActive,
                MemberCount = memberCounts.GetValueOrDefault(organization.Id),
                EnabledAppCount = appCounts.GetValueOrDefault(organization.Id),
                CreatedAt = organization.CreatedAt,
                CreatedBy = organization.CreatedBy,
                CreatedByName = auditNames.GetValueOrDefault(organization.CreatedBy),
                ModifiedAt = organization.ModifiedAt,
                ModifiedBy = organization.ModifiedBy,
                ModifiedByName = organization.ModifiedBy.HasValue
                    ? auditNames.GetValueOrDefault(organization.ModifiedBy.Value)
                    : null
            };
        }).ToList();

        return new PagedOrganizationsDto
        {
            Organizations = organizationDtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
