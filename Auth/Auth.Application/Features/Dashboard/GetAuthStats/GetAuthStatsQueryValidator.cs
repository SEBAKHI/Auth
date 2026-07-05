using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetAuthStats;

/// <summary>
/// Validates the GetAuthStatsQuery input fields.
/// </summary>
public class GetAuthStatsQueryValidator : AbstractValidator<GetAuthStatsQuery>
{
    public GetAuthStatsQueryValidator()
    {
        RuleFor(x => x.Days).IsValidTrailingWindowDays();
    }
}
