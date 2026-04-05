using FluentValidation;

namespace Auth.Application.Features.Authentication.ExternalLogin;

/// <summary>
/// Validates the ExternalLoginCommand input fields.
/// </summary>
public class ExternalLoginCommandValidator : AbstractValidator<ExternalLoginCommand>
{
    public ExternalLoginCommandValidator()
    {
        RuleFor(x => x.Provider)
            .NotEmpty().WithMessage("Validation.Provider.Required");

        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("Validation.IdToken.Required");
    }
}
