using Auth.Domain.Entities;
using FluentValidation;

namespace Auth.Application.Features.Users.UiPreferences;

/// <summary>
/// Shape checks for a preference write.
///
/// The key is an allow-list, not a free string: this endpoint is writable by
/// any authenticated caller, so without a namespace and a length cap it is a
/// general-purpose storage service that happens to sit behind a login. The
/// per-user key count is checked in the handler, since it needs a query.
/// </summary>
public class SetMyUiPreferenceCommandValidator : AbstractValidator<SetMyUiPreferenceCommand>
{
    /// <summary>
    /// `table:` plus a table id. Matches the ids the client already passes to
    /// the shared table component (lowercase, digits, hyphens).
    /// </summary>
    public const string KeyPattern = @"^table:[a-z0-9-]{1,60}$";

    public SetMyUiPreferenceCommandValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(UserUiPreference.MaxKeyLength)
            .Matches(KeyPattern)
            .WithMessage($"Key must match {KeyPattern}.");

        RuleFor(x => x.Value)
            .NotEmpty()
            .MaximumLength(UserUiPreference.MaxValueLength);
    }
}
