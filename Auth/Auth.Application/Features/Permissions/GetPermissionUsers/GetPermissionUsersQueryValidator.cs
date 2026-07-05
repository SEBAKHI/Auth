using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Permissions.GetPermissionUsers;

/// <summary>
/// Validates the GetPermissionUsersQuery input fields.
/// </summary>
public class GetPermissionUsersQueryValidator : AbstractValidator<GetPermissionUsersQuery>
{
    public GetPermissionUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).IsValidPageNumber();
        RuleFor(x => x.PageSize).IsValidPageSize();
        RuleFor(x => x.SearchTerm)
            .MaximumLength(200).WithMessage("Validation.SearchTerm.MaxLength")
            .When(x => x.SearchTerm is not null);
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.PermissionUsers.Allowed);
    }
}
