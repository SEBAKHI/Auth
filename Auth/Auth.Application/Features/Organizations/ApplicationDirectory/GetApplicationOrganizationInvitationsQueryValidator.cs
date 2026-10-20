using FluentValidation;

namespace Auth.Application.Features.Organizations.ApplicationDirectory;

/// <summary>
/// Validates the paging of <see cref="GetApplicationOrganizationInvitationsQuery"/>. Statuses
/// arrive already parsed: the endpoint accepts only exact status names, one per value.
/// </summary>
public sealed class GetApplicationOrganizationInvitationsQueryValidator : AbstractValidator<GetApplicationOrganizationInvitationsQuery>
{
    public GetApplicationOrganizationInvitationsQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsDirectoryPageNumber();
        RuleFor(x => x.PageSize).IsDirectoryPageSize();
    }
}
