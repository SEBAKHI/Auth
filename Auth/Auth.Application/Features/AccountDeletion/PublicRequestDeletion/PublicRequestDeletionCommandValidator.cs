using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.PublicRequestDeletion;

/// <summary>
/// Validates the PublicRequestDeletionCommand input fields.
/// </summary>
public class PublicRequestDeletionCommandValidator : AbstractValidator<PublicRequestDeletionCommand>
{
    public PublicRequestDeletionCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Validation.Email.Required")
            .EmailAddress().WithMessage("Validation.Email.InvalidFormat");
    }
}
