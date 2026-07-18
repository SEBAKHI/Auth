namespace Auth.Application.Interfaces;

/// <summary>
/// In-process wake-up channel between enqueue and the outbox dispatcher.
/// On IIS shared hosting there is no external queue: signaling on enqueue makes
/// mail triggered by a request dispatch within that same process lifetime,
/// without depending on long-lived polling surviving app-pool idling.
/// </summary>
public interface INotificationDispatchSignal
{
    /// <summary>
    /// Wakes the dispatcher (non-blocking; coalesces repeated signals).
    /// </summary>
    void Notify();

    /// <summary>
    /// Waits until a signal arrives or the timeout elapses.
    /// </summary>
    Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
