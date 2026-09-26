using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.RefreshToken;

/// <summary>
/// Validates the RefreshTokenCommand input fields.
/// </summary>
public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithErrorCode(AuthErrors.RefreshTokenRequired.Code);
    }
}
