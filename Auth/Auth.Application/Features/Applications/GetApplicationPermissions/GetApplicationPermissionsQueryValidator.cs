using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Applications.GetApplicationPermissions;

/// <summary>
/// Validates the GetApplicationPermissionsQuery input fields.
/// </summary>
public class GetApplicationPermissionsQueryValidator : AbstractValidator<GetApplicationPermissionsQuery>
{
    public GetApplicationPermissionsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Permissions.Allowed);
    }
}
