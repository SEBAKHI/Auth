using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ServerSignOut;

/// <summary>
/// Validates the <see cref="ServerSignOutCommand"/> input fields. The client assertion is the
/// handler's to check: a missing one is a failed client authentication, with its own answer.
/// </summary>
public class ServerSignOutCommandValidator : AbstractValidator<ServerSignOutCommand>
{
    public ServerSignOutCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode(AuthErrors.RefreshTokenRequired.Code);
    }
}
