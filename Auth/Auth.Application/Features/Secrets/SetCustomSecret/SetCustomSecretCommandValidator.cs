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
            .NotEmpty().WithMessage("Secret key is required.")
            .MaximumLength(100).WithMessage("Secret key must not exceed 100 characters.")
            .Matches("^[a-zA-Z0-9_.]+$").WithMessage("Secret key must be alphanumeric with underscores or dots only.");

        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Secret value is required.");
    }
}
