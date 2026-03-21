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
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(500).WithMessage("Reason must not exceed 500 characters.");
        RuleFor(x => x.LockDurationMinutes)
            .GreaterThan(0).WithMessage("Lock duration must be greater than 0.")
            .When(x => x.LockDurationMinutes.HasValue);
    }
}
