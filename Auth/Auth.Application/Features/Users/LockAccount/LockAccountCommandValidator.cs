using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(UserErrors.LockReasonRequired.Code)
            .MaximumLength(500).WithErrorCode(UserErrors.LockReasonTooLong.Code);
        RuleFor(x => x.LockDurationMinutes)
            .GreaterThan(0).WithErrorCode(UserErrors.LockDurationNotPositive.Code)
            .When(x => x.LockDurationMinutes.HasValue);
    }
}
