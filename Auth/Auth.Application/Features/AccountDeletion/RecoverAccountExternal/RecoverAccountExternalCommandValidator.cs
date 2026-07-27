using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RecoverAccountExternal;

/// <summary>
/// Validates the RecoverAccountExternalCommand input fields.
/// </summary>
public class RecoverAccountExternalCommandValidator : AbstractValidator<RecoverAccountExternalCommand>
{
    public RecoverAccountExternalCommandValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("Validation.Provider.Required")
            .MaximumLength(50).WithMessage("Validation.Provider.TooLong");

        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("Validation.IdToken.Required");
    }
}
