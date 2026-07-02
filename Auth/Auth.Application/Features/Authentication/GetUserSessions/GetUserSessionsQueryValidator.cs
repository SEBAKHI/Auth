using Auth.Application.Validators.Rules;
using Auth.Domain.Constants;
using FluentValidation;

namespace Auth.Application.Features.Authentication.GetUserSessions;

/// <summary>
/// Validates the GetUserSessionsQuery input fields.
/// </summary>
public class GetUserSessionsQueryValidator : AbstractValidator<GetUserSessionsQuery>
{
    public GetUserSessionsQueryValidator()
    {
        RuleFor(x => x.SortBy).IsValidSortField(SortFields.Sessions.Allowed);
    }
}
