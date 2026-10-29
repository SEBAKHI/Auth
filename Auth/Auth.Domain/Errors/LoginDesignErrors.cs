using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors of the sign-in page designs: the HTML and CSS an application, or the
/// platform, shows in the panel half of the sign-in pages.
/// </summary>
/// <remarks>
/// A Liquid syntax error is the renderer's own <c>Notification.InvalidTemplateSyntax</c>: one
/// rule (the source parses), one declaration, whichever kind of template it is.
/// </remarks>
public static class LoginDesignErrors
{
    public static Error NotFound(Guid designId) => Error.NotFound(
        code: "LoginDesign.NotFound",
        description: $"Sign-in page design with ID '{designId}' was not found.",
        metadata: new() { ["args"] = new object[] { designId } });

    public static readonly Error NameRequired = Error.Validation(
        code: "LoginDesign.NameRequired",
        description: "The design needs a name.");

    public static readonly Error NameTooLong = Error.Validation(
        code: "LoginDesign.NameTooLong",
        description: "The design's name must not exceed 200 characters.");

    public static readonly Error ContentRequired = Error.Validation(
        code: "LoginDesign.ContentRequired",
        description: "The design needs content.");

    /// <summary>Over <see cref="Entities.LoginDesign.MaxContentBytes"/> of UTF-8 (64 KB).</summary>
    public static readonly Error ContentTooLarge = Error.Validation(
        code: "LoginDesign.ContentTooLarge",
        description: "The design's content must not exceed 64 KB.");

    public static readonly Error StringsInvalidJson = Error.Validation(
        code: "LoginDesign.StringsInvalidJson",
        description: "The design's strings must be a JSON object of languages, each an object of text strings.");

    /// <summary>Over <see cref="Entities.LoginDesign.MaxStringsBytes"/> of UTF-8 (64 KB).</summary>
    public static readonly Error StringsTooLarge = Error.Validation(
        code: "LoginDesign.StringsTooLarge",
        description: "The design's strings must not exceed 64 KB.");

    /// <summary>
    /// The design names a variable a sign-in page does not supply. Checked when it is saved, in
    /// every supported language, so the gap never reaches a visitor as an empty space.
    /// </summary>
    public static Error UnknownVariables(string variableNames) => Error.Validation(
        code: "LoginDesign.UnknownVariables",
        description: $"The design uses variables a sign-in page does not supply: {variableNames}",
        metadata: new() { ["args"] = new object[] { variableNames } });

    /// <summary>
    /// The design renders over <see cref="Entities.LoginDesign.MaxRenderedBytes"/> of UTF-8 HTML
    /// (256 KB) in at least one language. Refused when it is saved or previewed; a page asking
    /// for a design that outgrew it (a longer application name) keeps the plain panel.
    /// </summary>
    public static readonly Error RenderedTooLarge = Error.Validation(
        code: "LoginDesign.RenderedTooLarge",
        description: "The design renders more than 256 KB of HTML in at least one language.");

    public static readonly Error UnsupportedLanguage = Error.Validation(
        code: "LoginDesign.UnsupportedLanguage",
        description: "This language is not supported.");

    /// <summary>
    /// The design visitors see cannot be deleted; another one is activated first, or this one
    /// is deactivated.
    /// </summary>
    public static readonly Error DeleteActive = Error.Conflict(
        code: "LoginDesign.DeleteActive",
        description: "The active design cannot be deleted. Deactivate it, or activate another one, first.");

    /// <summary>
    /// Another activation in the same scope committed first: one design at most is active per
    /// application, and one for the platform, and the database refused the second.
    /// </summary>
    public static readonly Error ActivationConflict = Error.Conflict(
        code: "LoginDesign.ActivationConflict",
        description: "Another design was activated at the same time. Reload the list and try again.");

    /// <summary>The design was saved by someone else since the editor loaded it.</summary>
    public static readonly Error ConcurrencyConflict = Error.Conflict(
        code: "LoginDesign.ConcurrencyConflict",
        description: "This design was changed by someone else. Reload it and try again.");

    /// <summary>
    /// No design shows on this page: the application has no active design and neither has
    /// the platform. The page keeps its plain panel.
    /// </summary>
    public static readonly Error NoneActive = Error.NotFound(
        code: "LoginDesign.NoneActive",
        description: "No sign-in page design is active.");
}
