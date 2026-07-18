using System.Threading.Channels;
using Auth.Application.Interfaces;

namespace Auth.Infrastructure.Notifications.Outbox;

/// <summary>
/// Bounded single-slot channel: repeated notifications coalesce into one
/// wake-up, and waiting honors the poll-interval timeout as a fallback.
/// </summary>
public class NotificationDispatchSignal : INotificationDispatchSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

    /// <inheritdoc />
    public void Notify()
    {
        _channel.Writer.TryWrite(true);
    }

    /// <inheritdoc />
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout elapsed — fall through to a regular poll cycle.
        }
    }
}
