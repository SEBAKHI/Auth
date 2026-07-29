using FluentValidation;

namespace Auth.Application.Features.AccountDeletion.RequestAccountDeletion;

/// <summary>
/// Validates the RequestAccountDeletionCommand input fields.
/// </summary>
public class RequestAccountDeletionCommandValidator : AbstractValidator<RequestAccountDeletionCommand>
{
    public RequestAccountDeletionCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Password) || !string.IsNullOrEmpty(x.OtpCode))
            .WithMessage("Validation.Reauthentication.Required");

        RuleFor(x => x.OtpCode)
            .Length(6).WithMessage("Validation.OtpCode.InvalidFormat")
            .When(x => !string.IsNullOrEmpty(x.OtpCode));
    }
}
