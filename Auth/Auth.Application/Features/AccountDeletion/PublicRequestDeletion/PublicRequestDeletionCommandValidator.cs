using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(EmailErrors.Required.Code)
            .EmailAddress().WithErrorCode(EmailErrors.InvalidFormat.Code);
    }
}
