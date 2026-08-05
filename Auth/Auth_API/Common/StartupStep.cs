using Serilog;

namespace Auth_API.Common;

/// <summary>
/// Runs a startup step that is allowed to abort the process, and makes sure the
/// reason reaches the log file before it does.
///
/// <para>
/// The host's own try/catch only wraps <c>app.Run()</c>, so anything thrown
/// while the builder is still being assembled — every fail-fast guard — dies
/// before Serilog is ever asked to write it. Under IIS that surfaces as a bare
/// "HTTP Error 500.30 — ASP.NET Core app failed to start" page with an empty
/// application log, and the operator's only route to the message is enabling
/// stdout capture and reproducing the crash.
/// </para>
///
/// <para>
/// A guard whose entire purpose is to explain what is missing is worthless if
/// its explanation is unreachable. This wrapper logs at Fatal and flushes
/// before rethrowing, so the message lands in the same file the operator is
/// already reading.
/// </para>
/// </summary>
public static class StartupStep
{
    public static void Run(string description, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup aborted during: {Step}", description);
            Log.CloseAndFlush();
            throw;
        }
    }
}
