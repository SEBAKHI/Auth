using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the languages an administrator hides from the language menus of both apps.
/// </summary>
public static class HiddenLanguagesErrors
{
    /// <summary>A code that is not one of the languages the apps ship.</summary>
    public static readonly Error UnsupportedLanguage = Error.Validation(
        code: "HiddenLanguages.UnsupportedLanguage",
        description: "Only a language the apps ship can be hidden.");

    /// <summary>
    /// English in the list: every page falls back to it, so it stays visible, and with it at
    /// least one language always does.
    /// </summary>
    public static readonly Error DefaultLanguageRequired = Error.Validation(
        code: "HiddenLanguages.DefaultLanguageRequired",
        description: "English cannot be hidden: it is the language every page falls back to.");
}
