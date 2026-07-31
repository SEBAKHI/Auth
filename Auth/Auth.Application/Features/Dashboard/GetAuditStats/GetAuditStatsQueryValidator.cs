using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetAuditStats;

/// <summary>
/// Validates the GetAuditStatsQuery input fields.
/// </summary>
public class GetAuditStatsQueryValidator : AbstractValidator<GetAuditStatsQuery>
{
    public GetAuditStatsQueryValidator()
    {
        RuleFor(x => x.Days).IsValidTrailingWindowDays();
        RuleFor(x => x.TimeZone).NotEmpty().IsValidTimeZone();
    }
}
