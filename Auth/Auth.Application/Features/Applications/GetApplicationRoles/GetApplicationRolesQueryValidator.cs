using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Applications.GetApplicationRoles;

/// <summary>
/// Validates the GetApplicationRolesQuery input fields.
/// </summary>
public class GetApplicationRolesQueryValidator : AbstractValidator<GetApplicationRolesQuery>
{
    public GetApplicationRolesQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Roles.Allowed);
    }
}
