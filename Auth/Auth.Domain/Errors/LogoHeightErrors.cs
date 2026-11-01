using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the height a logo is shown at on the sign-in pages, for the platform's logo and an
/// application's alike.
/// </summary>
public static class LogoHeightErrors
{
    public static Error OutOfRange(int minPx, int maxPx) => Error.Validation(
        code: "LogoHeight.OutOfRange",
        description: $"A logo's height must be a whole number of pixels from {minPx} to {maxPx}.",
        metadata: new() { ["args"] = new object[] { minPx, maxPx } });
}
