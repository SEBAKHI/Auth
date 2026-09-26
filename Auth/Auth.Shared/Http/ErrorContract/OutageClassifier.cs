namespace Auth.Shared.Http.ErrorContract;

/// <summary>
/// Which unhandled exceptions mean a dependency is unreachable rather than that the code is
/// wrong (ADR 0001, section 5). An outage is answered with 503 and <c>Retry-After</c>, so a
/// client retries later instead of reporting a fault.
/// </summary>
/// <remarks>
/// <see cref="HttpClient"/>'s own timeout throws a <see cref="TaskCanceledException"/> whose
/// inner exception is a <see cref="TimeoutException"/>. A database outage is the host's
/// <see cref="IExceptionProblemTranslator"/> for its driver's exception type, since this project
/// references no driver.
/// </remarks>
public static class OutageClassifier
{
    public static bool IsOutage(Exception exception) =>
        exception is HttpRequestException or TimeoutException
        || exception is TaskCanceledException { InnerException: TimeoutException };
}
