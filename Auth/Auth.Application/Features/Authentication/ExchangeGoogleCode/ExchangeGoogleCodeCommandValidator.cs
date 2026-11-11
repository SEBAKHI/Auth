using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Authentication.ExchangeGoogleCode;

/// <summary>
/// Validates <see cref="ExchangeGoogleCodeCommand"/> before anything is sent to Google.
/// </summary>
public class ExchangeGoogleCodeCommandValidator : AbstractValidator<ExchangeGoogleCodeCommand>
{
    /// <summary>
    /// RFC 7636 §4.1: 43 to 128 characters of the unreserved set
    /// (<c>A-Z a-z 0-9 - . _ ~</c>). The end anchor is <c>\z</c>, not <c>$</c>: .NET's <c>$</c>
    /// also matches before a final line break, which would let a verifier ending in one through.
    /// </summary>
    private const string CodeVerifierPattern = @"^[A-Za-z0-9._~-]{43,128}\z";

    public ExchangeGoogleCodeCommandValidator()
    {
        // Forwarded to Google's token endpoint, so bounded here. Google's codes are a few
        // dozen characters; 512 leaves room for a format change and refuses a payload.
        RuleFor(x => x.Code)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.CodeRequired.Code)
            .MaximumLength(512).WithErrorCode(ExternalAuthErrors.CodeTooLong.Code);

        // NotEmpty first: Matches lets a missing value through.
        RuleFor(x => x.CodeVerifier)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(ExternalAuthErrors.CodeVerifierInvalid.Code)
            .Matches(CodeVerifierPattern).WithErrorCode(ExternalAuthErrors.CodeVerifierInvalid.Code);
    }
}
