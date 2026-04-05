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
            .NotEmpty().WithMessage("Validation.SecretKey.Required")
            .MaximumLength(100).WithMessage("Validation.SecretKey.MaxLength")
            .Matches("^[a-zA-Z0-9_.]+$").WithMessage("Validation.SecretKey.InvalidFormat");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Validation.SecretValue.Required");
    }
}
