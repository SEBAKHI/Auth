using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Errors;
using FluentValidation;

namespace Auth.Application.Features.LoginDesigns.Common;

/// <summary>
/// The request rules of a design's fields, shared by every command that carries them. Each rule
/// asks the entity (<see cref="LoginDesign.CheckName"/>, <see cref="LoginDesign.CheckContent"/>,
/// <see cref="LoginDesign.CheckStrings"/>), so the limits are declared once.
/// </summary>
public static class LoginDesignRules
{
    public static IRuleBuilderOptions<T, string?> ValidDesignName<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(LoginDesignErrors.NameRequired.Code)
            .Must(name => !LoginDesign.CheckName(name).IsError)
            .WithErrorCode(LoginDesignErrors.NameTooLong.Code);

    public static IRuleBuilderOptions<T, string?> ValidDesignContent<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(LoginDesignErrors.ContentRequired.Code)
            .Must(content => !LoginDesign.CheckContent(content).IsError)
            .WithErrorCode(LoginDesignErrors.ContentTooLarge.Code);

    public static IRuleBuilderOptions<T, string?> ValidDesignStrings<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .Must(strings => strings is null || System.Text.Encoding.UTF8.GetByteCount(strings) <= LoginDesign.MaxStringsBytes)
            .WithErrorCode(LoginDesignErrors.StringsTooLarge.Code)
            .Must(strings => !LoginDesign.CheckStrings(strings).IsError)
            .WithErrorCode(LoginDesignErrors.StringsInvalidJson.Code);

    public static IRuleBuilderOptions<T, string?> SupportedDesignLanguage<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .Must(Languages.IsSupported)
            .WithErrorCode(LoginDesignErrors.UnsupportedLanguage.Code);
}
