using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on rotating an API key or a webhook key.
/// </summary>
public static class KeyRotationErrors
{
    public static readonly Error GracePeriodNegative = Error.Validation(
        code: "KeyRotation.GracePeriodNegative",
        description: "Grace period must be 0 or greater.");
}
