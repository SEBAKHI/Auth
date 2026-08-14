using FluentValidation;

namespace Auth.Application.Features.Applications.GrantApplicationAccess;

/// <summary>
/// Validates the GrantApplicationAccessCommand input fields.
/// </summary>
public class GrantApplicationAccessCommandValidator : AbstractValidator<GrantApplicationAccessCommand>
{
    public GrantApplicationAccessCommandValidator()
    {
        RuleFor(x => x.ApplicationId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();

        // An invitation that has already lapsed admits nobody, so accepting one
        // would only produce a row that silently does nothing.
        RuleFor(x => x.ExpiresAt!.Value)
            .GreaterThan(_ => DateTime.UtcNow)
            .When(x => x.ExpiresAt.HasValue);

        RuleFor(x => x.Note!)
            .MaximumLength(500)
            .When(x => x.Note is not null);
    }
}
