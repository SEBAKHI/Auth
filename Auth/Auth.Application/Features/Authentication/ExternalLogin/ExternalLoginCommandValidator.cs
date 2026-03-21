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
            .NotEmpty().WithMessage("Provider is required.");

        RuleFor(x => x.IdToken)
            .NotEmpty().WithMessage("ID token is required.");
    }
}
