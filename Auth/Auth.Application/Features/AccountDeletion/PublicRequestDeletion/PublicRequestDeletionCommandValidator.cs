using Auth.Application.Validators.Rules;
using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.PublicRequestDeletion;

/// <summary>
/// Validates the PublicRequestDeletionCommand input fields.
/// </summary>
public class PublicRequestDeletionCommandValidator : AbstractValidator<PublicRequestDeletionCommand>
{
    public PublicRequestDeletionCommandValidator()
    {
        RuleFor(x => x.Email).IsValidEmail();
    }
}
