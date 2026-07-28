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
}
