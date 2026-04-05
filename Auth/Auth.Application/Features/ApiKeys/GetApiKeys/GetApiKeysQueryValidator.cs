using FluentValidation;

namespace Auth.Application.Features.ApiKeys.GetApiKeys;

/// <summary>
/// Validates the GetApiKeysQuery input fields.
/// </summary>
public class GetApiKeysQueryValidator : AbstractValidator<GetApiKeysQuery>
{
    public GetApiKeysQueryValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty().WithMessage("Validation.ApplicationId.Required");
    }
}
