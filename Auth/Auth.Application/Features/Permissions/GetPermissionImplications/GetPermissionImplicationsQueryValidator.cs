using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Permissions.GetPermissionImplications;

/// <summary>
/// Validates the GetPermissionImplicationsQuery input fields.
/// </summary>
public class GetPermissionImplicationsQueryValidator : AbstractValidator<GetPermissionImplicationsQuery>
{
    public GetPermissionImplicationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.PermissionImplications.Allowed);
    }
}
