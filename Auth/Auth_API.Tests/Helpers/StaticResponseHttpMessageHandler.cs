using System.Net;
using System.Text;

namespace Auth_API.Tests.Helpers;

/// <summary>
/// HttpMessageHandler that answers every request with the same status, body and media type, with
/// no network: what a proxy's block page or a captive portal looks like to an HttpClient.
/// </summary>
public sealed class StaticResponseHttpMessageHandler(
    HttpStatusCode status, string body, string mediaType = "text/plain") : HttpMessageHandler
{
    public int Requests { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        });
    }
}
