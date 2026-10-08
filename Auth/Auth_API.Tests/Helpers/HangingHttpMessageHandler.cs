namespace Auth_API.Tests.Helpers;

/// <summary>
/// HttpMessageHandler that never answers: it waits on the token it is given, so the request ends
/// only when <see cref="HttpClient.Timeout"/> or the caller cancels. Lets a test see the exception
/// the runtime really throws for a timeout, with no network.
/// </summary>
public sealed class HangingHttpMessageHandler : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
    }
}
