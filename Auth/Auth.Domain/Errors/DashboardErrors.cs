using ErrorOr;

namespace Auth.Domain.Errors;

/// <summary>
/// Rules on the parameters of the dashboard statistics.
/// </summary>
public static class DashboardErrors
{
    public static readonly Error DaysOutOfRange = Error.Validation(
        code: "Dashboard.DaysOutOfRange",
        description: "Days must be between 1 and 90.");

    public static readonly Error HorizonDaysOutOfRange = Error.Validation(
        code: "Dashboard.HorizonDaysOutOfRange",
        description: "The horizon must be between 1 and 365 days.");

    public static readonly Error TimeZoneInvalid = Error.Validation(
        code: "Dashboard.TimeZoneInvalid",
        description: "Time zone must be a valid IANA identifier (e.g. Asia/Riyadh).");

    public static readonly Error TimeZoneRequired = Error.Validation(
        code: "Dashboard.TimeZoneRequired",
        description: "Time zone is required.");
}
