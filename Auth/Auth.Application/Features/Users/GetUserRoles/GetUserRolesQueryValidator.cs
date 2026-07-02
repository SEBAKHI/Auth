using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Users.GetUserRoles;

/// <summary>
/// Validates the GetUserRolesQuery input fields.
/// </summary>
public class GetUserRolesQueryValidator : AbstractValidator<GetUserRolesQuery>
{
    public GetUserRolesQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.UserRoles.Allowed);
    }
}
