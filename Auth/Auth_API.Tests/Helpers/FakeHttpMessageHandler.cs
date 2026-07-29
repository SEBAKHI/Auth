namespace Auth_API.Tests.Helpers;

/// <summary>
/// HttpMessageHandler fake for typed-client tests: routes every request
/// through the supplied responder and records it for assertions.
/// </summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responder(request));
    }
}
