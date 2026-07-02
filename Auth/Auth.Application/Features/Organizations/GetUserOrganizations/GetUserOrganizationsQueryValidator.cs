using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Organizations.GetUserOrganizations;

/// <summary>
/// Validates the GetUserOrganizationsQuery input fields.
/// </summary>
public class GetUserOrganizationsQueryValidator : AbstractValidator<GetUserOrganizationsQuery>
{
    public GetUserOrganizationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.UserOrganizations.Allowed);
    }
}
