using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Roles.GetRoles;

/// <summary>
/// Validates the GetRolesQuery input fields.
/// </summary>
public class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Roles.Allowed);
    }
}
