using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.ConfirmPublicDeletion;

/// <summary>
/// Validates the ConfirmPublicDeletionCommand input fields.
/// </summary>
public class ConfirmPublicDeletionCommandValidator : AbstractValidator<ConfirmPublicDeletionCommand>
{
    public ConfirmPublicDeletionCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat");

        RuleFor(x => x.OtpCode)
            .NotEmpty().WithMessage("Validation.OtpCode.Required")
            .Length(6).WithMessage("Validation.OtpCode.InvalidFormat");
    }
}
