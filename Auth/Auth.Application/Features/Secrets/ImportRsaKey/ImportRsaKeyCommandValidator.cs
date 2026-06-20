using FluentValidation;

namespace Auth.Application.Features.Secrets.ImportRsaKey;

/// <summary>
/// Validates the shape of the imported RSA private key. Cryptographic validity
/// (parseable PEM, key size, presence of the private key) is checked in the handler.
/// </summary>
public class ImportRsaKeyCommandValidator : AbstractValidator<ImportRsaKeyCommand>
{
    public ImportRsaKeyCommandValidator()
    {
        RuleFor(x => x.PrivateKeyPem)
            .NotEmpty().WithMessage("Validation.RsaPrivateKey.Required");
    }
}
