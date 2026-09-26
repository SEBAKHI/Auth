using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetUserStats;

/// <summary>
/// Validates the GetUserStatsQuery input fields.
/// </summary>
public class GetUserStatsQueryValidator : AbstractValidator<GetUserStatsQuery>
{
    public GetUserStatsQueryValidator()
    {
        RuleFor(x => x.Days).IsValidTrailingWindowDays();
        RuleFor(x => x.TimeZone)
            .NotEmpty().WithErrorCode(DashboardErrors.TimeZoneRequired.Code)
            .IsValidTimeZone(DashboardErrors.TimeZoneInvalid);
    }
}
