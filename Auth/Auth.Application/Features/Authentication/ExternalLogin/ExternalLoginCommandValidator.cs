using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(ExternalAuthErrors.ProviderRequired.Code);

        RuleFor(x => x.IdToken)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.IdTokenRequired.Code);
    }
}
