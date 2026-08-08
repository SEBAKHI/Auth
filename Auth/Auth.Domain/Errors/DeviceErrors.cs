using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Domain errors for the browsers a user has signed in from.
/// </summary>
public static class DeviceErrors
{
    /// <summary>
    /// Also returned when the device belongs to someone else. Distinguishing
    /// "not yours" from "does not exist" would turn the endpoint into an oracle
    /// for whether an id is real, so both answer the same way — the same choice
    /// TerminateSessionCommandHandler makes for sessions.
    /// </summary>
    public static Error NotFound => Error.NotFound(
        code: "Device.NotFound",
        description: "The device was not found.");

    /// <summary>
    /// Forgetting the browser you are reading this on would sign you out from a
    /// control that does not say so. Ending the current session is what the
    /// ordinary sign-out is for.
    /// </summary>
    public static Error CannotForgetCurrent => Error.Validation(
        code: "Device.CannotForgetCurrent",
        description: "You cannot forget the browser you are currently signed in on. Sign out instead.");
}
