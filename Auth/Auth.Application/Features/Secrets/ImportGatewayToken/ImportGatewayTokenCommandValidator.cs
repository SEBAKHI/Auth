using FluentValidation;

namespace Auth.Application.Features.Secrets.ImportGatewayToken;

/// <summary>
/// Validates the imported gateway token. The token is compared as an opaque string at runtime,
/// so only non-emptiness and a minimum length are enforced to reject trivially weak tokens.
/// </summary>
public class ImportGatewayTokenCommandValidator : AbstractValidator<ImportGatewayTokenCommand>
{
    public ImportGatewayTokenCommandValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Validation.GatewayToken.Required")
            .MinimumLength(16).WithMessage("Validation.GatewayToken.MinLength");
    }
}
