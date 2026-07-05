using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetSessionStats;

/// <summary>
/// Validates the GetSessionStatsQuery input fields.
/// </summary>
public class GetSessionStatsQueryValidator : AbstractValidator<GetSessionStatsQuery>
{
    public GetSessionStatsQueryValidator()
    {
        RuleFor(x => x.Days).IsValidTrailingWindowDays();
    }
}
