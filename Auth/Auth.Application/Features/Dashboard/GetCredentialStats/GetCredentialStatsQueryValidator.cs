using FluentValidation;

namespace Auth.Application.Features.Dashboard.GetCredentialStats;

/// <summary>
/// Validates the GetCredentialStatsQuery input fields.
/// </summary>
public class GetCredentialStatsQueryValidator : AbstractValidator<GetCredentialStatsQuery>
{
    public GetCredentialStatsQueryValidator()
    {
        // Not IsValidTrailingWindowDays: that rule guards a window into the past and
        // its allowed values are a different set. This one bounds a forward horizon.
        RuleFor(x => x.HorizonDays)
            .InclusiveBetween(1, 365)
            .WithMessage("Validation.HorizonDays.Range");

        RuleFor(x => x.RequestedBy)
            .NotEmpty()
            .WithMessage("Validation.UserId.Required");
    }
}
