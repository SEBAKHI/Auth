using Auth.Domain.Errors;
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
            .NotEmpty().WithErrorCode(ExternalAuthErrors.ProviderRequired.Code)
            .MaximumLength(50).WithErrorCode(ExternalAuthErrors.ProviderTooLong.Code);

        RuleFor(x => x.IdToken)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.IdTokenRequired.Code);
    }
}
