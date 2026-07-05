using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Users.GetUserApplications;

/// <summary>
/// Validates the GetUserApplicationsQuery input fields.
/// </summary>
public class GetUserApplicationsQueryValidator : AbstractValidator<GetUserApplicationsQuery>
{
    public GetUserApplicationsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.UserApplications.Allowed);
    }
}
