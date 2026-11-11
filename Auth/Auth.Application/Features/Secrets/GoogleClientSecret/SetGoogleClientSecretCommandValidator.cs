using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Secrets.GoogleClientSecret;

/// <summary>
/// Validates <see cref="SetGoogleClientSecretCommand"/>.
/// </summary>
/// <remarks>
/// Length only, as for the SMTP password: the format is Google's to decide and has changed
/// before, so anything stricter could refuse a valid secret. A wrong value shows as
/// <c>ExternalAuth.ProviderNotConfigured</c> on the first Google sign-in after the restart,
/// and the API log names Google's reason (<c>invalid_client</c>).
/// </remarks>
public class SetGoogleClientSecretCommandValidator : AbstractValidator<SetGoogleClientSecretCommand>
{
    public SetGoogleClientSecretCommandValidator()
    {
        RuleFor(x => x.Value)
            .NotEmpty().WithErrorCode(SecretErrors.ValueRequired.Code)
            .MaximumLength(512).WithErrorCode(SecretErrors.ValueTooLong.Code);
    }
}
