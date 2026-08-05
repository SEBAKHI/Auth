using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors for the privacy-policy revision registry.
/// </summary>
public static class PrivacyPolicyErrors
{
    public static Error NotFound(string version) => Error.NotFound(
        code: "PrivacyPolicy.NotFound",
        description: $"Privacy policy version '{version}' was not found.");

    public static Error DuplicateVersion(string version) => Error.Conflict(
        code: "PrivacyPolicy.DuplicateVersion",
        description: $"Privacy policy version '{version}' is already recorded.");

    public static Error VersionLocked(string version) => Error.Conflict(
        code: "PrivacyPolicy.VersionLocked",
        description:
            $"Version '{version}' cannot be renamed: it is published or its change notice was already sent.");

    public static Error NoPublishedVersion => Error.NotFound(
        code: "PrivacyPolicy.NoPublishedVersion",
        description: "No privacy policy version has been published yet.");

    public static Error InvalidContent(string reason) => Error.Validation(
        code: "PrivacyPolicy.InvalidContent",
        description: $"The policy document is invalid: {reason}");

    public static Error UnsupportedLanguage(string languageCode) => Error.Validation(
        code: "PrivacyPolicy.UnsupportedLanguage",
        description: $"Language '{languageCode}' is not a supported policy language.");

    /// <summary>
    /// Publishing is refused while the policy cannot name its own controller.
    /// KVKK Art. 10 and GDPR Art. 13(1)(a) require the controller's identity,
    /// and a rights request cannot be addressed to a placeholder.
    /// </summary>
    public static Error ControllerDetailsIncomplete(string fields) => Error.Validation(
        code: "PrivacyPolicy.ControllerDetailsIncomplete",
        description:
            "The policy cannot be published until the data-controller details are filled in " +
            $"(System Settings -> Data controller). Missing or placeholder: {fields}.");
}
