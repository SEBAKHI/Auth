using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Roles.GetRoleApplications;

/// <summary>
/// Validates the GetRoleApplicationsQuery input fields.
/// </summary>
public class GetRoleApplicationsQueryValidator : AbstractValidator<GetRoleApplicationsQuery>
{
    public GetRoleApplicationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.RoleApplications.Allowed);
    }
}
