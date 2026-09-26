using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.PrivacyPolicy.CreatePrivacyPolicyVersion;

/// <summary>
/// Validates the CreatePrivacyPolicyVersionCommand input fields.
/// </summary>
public class CreatePrivacyPolicyVersionCommandValidator
    : AbstractValidator<CreatePrivacyPolicyVersionCommand>
{
    public CreatePrivacyPolicyVersionCommandValidator()
    {
        RuleFor(x => x.Version)
            .NotEmpty().WithErrorCode(PrivacyPolicyErrors.VersionRequired.Code)
            .Matches(@"^\d{4}\.\d{2}$").WithErrorCode(PrivacyPolicyErrors.VersionInvalidFormat.Code);
    }
}
