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
            .NotEmpty().WithErrorCode(ExternalAuthErrors.ProviderRequired.Code)
            .MaximumLength(50).WithErrorCode(ExternalAuthErrors.ProviderTooLong.Code);

        RuleFor(x => x.IdToken)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.IdTokenRequired.Code);

        // Forwarded to the provider's token endpoint (Apple), so bounded here.
        RuleFor(x => x.AuthorizationCode)
            .MaximumLength(2000).WithErrorCode(ExternalAuthErrors.AuthorizationCodeTooLong.Code)
            .When(x => x.AuthorizationCode is not null);

        // Stored as the new account's name when the provider sends none; the
        // columns are NVARCHAR(100).
        RuleFor(x => x.GivenName)
            .MaximumLength(100).WithErrorCode(ExternalAuthErrors.GivenNameTooLong.Code)
            .When(x => x.GivenName is not null);

        RuleFor(x => x.FamilyName)
            .MaximumLength(100).WithErrorCode(ExternalAuthErrors.FamilyNameTooLong.Code)
            .When(x => x.FamilyName is not null);
    }
}
