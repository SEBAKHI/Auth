using Auth.Application.Validators.Rules;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.RevokeToken;

/// <summary>
/// Validates the RevokeTokenCommand input fields.
/// </summary>
public class RevokeTokenCommandValidator : AbstractValidator<RevokeTokenCommand>
{
    public RevokeTokenCommandValidator()
    {
        RuleFor(x => x.Token).IsRequiredToken(AuthErrors.TokenRequired);
    }
}
