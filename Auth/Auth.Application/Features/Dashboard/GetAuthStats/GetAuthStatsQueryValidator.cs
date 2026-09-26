using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
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
        RuleFor(x => x.TimeZone)
            .NotEmpty().WithErrorCode(DashboardErrors.TimeZoneRequired.Code)
            .IsValidTimeZone(DashboardErrors.TimeZoneInvalid);
    }
}
