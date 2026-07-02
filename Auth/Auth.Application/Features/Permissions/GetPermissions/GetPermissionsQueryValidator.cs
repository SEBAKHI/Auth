using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Permissions.GetPermissions;

/// <summary>
/// Validates the GetPermissionsQuery input fields.
/// </summary>
public class GetPermissionsQueryValidator : AbstractValidator<GetPermissionsQuery>
{
    public GetPermissionsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Permissions.Allowed);
    }
}
