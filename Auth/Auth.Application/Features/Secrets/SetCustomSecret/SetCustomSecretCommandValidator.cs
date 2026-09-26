using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.SetCustomSecret;

/// <summary>
/// Validates the SetCustomSecretCommand input fields.
/// </summary>
public class SetCustomSecretCommandValidator : AbstractValidator<SetCustomSecretCommand>
{
    public SetCustomSecretCommandValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithErrorCode(SecretErrors.KeyRequired.Code)
            .MaximumLength(100).WithErrorCode(SecretErrors.KeyTooLong.Code)
            .Matches("^[a-zA-Z0-9_.]+$").WithErrorCode(SecretErrors.KeyInvalidFormat.Code);

        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode(SecretErrors.ValueRequired.Code);
    }
}
