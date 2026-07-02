using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Users.GetUserPermissions;

/// <summary>
/// Validates the GetUserPermissionsQuery input fields.
/// </summary>
public class GetUserPermissionsQueryValidator : AbstractValidator<GetUserPermissionsQuery>
{
    public GetUserPermissionsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.UserPermissions.Allowed);
    }
}
