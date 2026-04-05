using FluentValidation;

namespace Auth.Application.Features.ApiKeys.RotateApiKey;

/// <summary>
/// Validates the RotateApiKeyCommand input fields.
/// </summary>
public class RotateApiKeyCommandValidator : AbstractValidator<RotateApiKeyCommand>
{
    public RotateApiKeyCommandValidator()
    {
        RuleFor(x => x.GracePeriodMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Validation.GracePeriod.NonNegative");
    }
}
