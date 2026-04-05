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
            .NotEmpty().WithMessage("Validation.RefreshToken.Required");
    }
}
