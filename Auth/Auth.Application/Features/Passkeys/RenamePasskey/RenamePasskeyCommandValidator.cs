using Auth.Application.Features.Passkeys.Common;
using FluentValidation;

namespace Auth.Application.Features.Passkeys.RenamePasskey;

/// <summary>Validates the RenamePasskeyCommand input fields.</summary>
public sealed class RenamePasskeyCommandValidator : AbstractValidator<RenamePasskeyCommand>
{
    public RenamePasskeyCommandValidator()
    {
        RuleFor(x => x.Name).IsValidPasskeyName();
    }
}
