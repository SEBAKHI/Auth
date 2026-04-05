using FluentValidation;

namespace Auth.Application.Features.Users.LockAccount;

/// <summary>
/// Validates the LockAccountCommand input fields.
/// </summary>
public class LockAccountCommandValidator : AbstractValidator<LockAccountCommand>
{
    public LockAccountCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Validation.Reason.Required")
            .MaximumLength(500).WithMessage("Validation.Reason.MaxLength");
        RuleFor(x => x.LockDurationMinutes)
            .GreaterThan(0).WithMessage("Validation.LockDuration.GreaterThanZero")
            .When(x => x.LockDurationMinutes.HasValue);
    }
}
