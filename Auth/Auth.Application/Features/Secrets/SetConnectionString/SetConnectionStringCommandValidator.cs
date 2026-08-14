using FluentValidation;

namespace Auth.Application.Features.Secrets.SetConnectionString;

/// <summary>
/// Validates the SetConnectionStringCommand input fields.
/// </summary>
/// <remarks>
/// Shape only. Whether the string parses, and whether it reaches a server, is
/// decided by the probe in the handler — that needs a database driver, which
/// this layer deliberately cannot reference.
/// </remarks>
public class SetConnectionStringCommandValidator : AbstractValidator<SetConnectionStringCommand>
{
    public SetConnectionStringCommandValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty().WithMessage("Validation.SecretValue.Required")
            .MaximumLength(2048).WithMessage("Validation.SecretValue.MaxLength");
    }
}
