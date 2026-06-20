using FluentValidation;

namespace Auth.Application.Features.Secrets.ImportHmacKey;

/// <summary>
/// Validates the shape of the imported HMAC key. The base64 decoding and minimum
/// length (256 bits) are verified in the handler.
/// </summary>
public class ImportHmacKeyCommandValidator : AbstractValidator<ImportHmacKeyCommand>
{
    public ImportHmacKeyCommandValidator()
    {
        RuleFor(x => x.HmacKeyBase64)
            .NotEmpty().WithMessage("Validation.HmacKey.Required");
    }
}
