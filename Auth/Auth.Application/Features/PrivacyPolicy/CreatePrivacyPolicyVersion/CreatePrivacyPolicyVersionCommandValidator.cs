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
            .NotEmpty().WithMessage("Validation.PolicyVersion.Required")
            .Matches(@"^\d{4}\.\d{2}$").WithMessage("Validation.PolicyVersion.InvalidFormat");
    }
}
