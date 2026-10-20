using FluentValidation;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>Validates the paging of <see cref="GetApplicationOrganizationMembersQuery"/>.</summary>
public sealed class GetApplicationOrganizationMembersQueryValidator : AbstractValidator<GetApplicationOrganizationMembersQuery>
{
    public GetApplicationOrganizationMembersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsDirectoryPageNumber();
        RuleFor(x => x.PageSize).IsDirectoryPageSize();
    }
}
