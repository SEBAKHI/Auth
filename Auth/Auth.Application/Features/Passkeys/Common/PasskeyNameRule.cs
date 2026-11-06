using Auth.Domain.Entities;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.Passkeys.Common;

/// <summary>
/// The rule a passkey's name obeys in every request that names one (ARR-0704): at most
/// <see cref="UserPasskey.MaxNameCodePoints"/> code points, counted by the entity's own rule,
/// with the published code <c>Passkey.NameTooLong</c>.
/// </summary>
public static class PasskeyNameRule
{
    /// <summary>A name the entity accepts: blank (a default is given), or at most 64 code points.</summary>
    public static IRuleBuilderOptions<T, string?> IsValidPasskeyName<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder
            .Must(name => !UserPasskey.NormalizeName(name).IsError)
            .WithErrorCode(PasskeyErrors.NameTooLong.Code)
            .WithMessage(PasskeyErrors.NameTooLong.Description);
}
