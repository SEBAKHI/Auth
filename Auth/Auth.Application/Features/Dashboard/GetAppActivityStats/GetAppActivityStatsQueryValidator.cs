using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetAppActivityStats;

/// <summary>
/// Validates the GetAppActivityStatsQuery input fields.
/// </summary>
public class GetAppActivityStatsQueryValidator : AbstractValidator<GetAppActivityStatsQuery>
{
    public GetAppActivityStatsQueryValidator()
    {
        RuleFor(x => x.Days).IsValidTrailingWindowDays();
    }
}
