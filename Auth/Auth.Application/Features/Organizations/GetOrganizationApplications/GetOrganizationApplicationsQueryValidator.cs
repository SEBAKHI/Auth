using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Organizations.GetOrganizationApplications;

/// <summary>
/// Validates the GetOrganizationApplicationsQuery input fields.
/// </summary>
public class GetOrganizationApplicationsQueryValidator : AbstractValidator<GetOrganizationApplicationsQuery>
{
    public GetOrganizationApplicationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.OrganizationApplications.Allowed);
    }
}
