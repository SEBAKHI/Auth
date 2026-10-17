using Auth.Application.Features.Authentication.ClassifyTokenClient;
using Auth.Shared.Http;
using Auth_API.Common.Middleware;
using Auth_API.Common.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// A token request whose body cannot be read (shorter than declared, a read that timed out) is
/// counted as it was before OI-178, never answered with a 500 by the classification: both
/// middlewares read the form before any limiter.
/// </summary>
public class TokenRequestBodyReadFailureTests
{
    /// <summary>A body that fails part-way, as Kestrel's does when the client sends less than it declared.</summary>
    private sealed class FailingBody : MemoryStream
    {
        public FailingBody() : base("grant_type=refresh_token&client_assertion=x"u8.ToArray()) { }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new BadHttpRequestException("Unexpected end of request content."));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(new BadHttpRequestException("Unexpected end of request content."));
    }

    private static DefaultHttpContext TokenRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.ContentLength = 4096;
        context.Request.Body = new FailingBody();
        return context;
    }

    [Fact]
    public async Task TheApi_CountsTheRequestOnItsAddress_AndLetsItGoOn()
    {
        var context = TokenRequest();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ClassifiesTokenClientAttribute()), "token"));
        var sender = new Mock<ISender>();
        var reachedNext = false;

        using var failures = new TokenAssertionFailureBudget(() => 0, () => new FixedWindowBudget(3, 60));
        await new TokenClientClassificationMiddleware(
                _ => { reachedNext = true; return Task.CompletedTask; }, failures, new TokenAssertionSightings(TimeProvider.System))
            .InvokeAsync(context, sender.Object);

        reachedNext.Should().BeTrue("model binding answers the unreadable form, as before");
        TokenRequestClass.Of(context).Should().BeSameAs(TokenRequestClass.ByAddress);
        sender.Verify(s => s.Send(It.IsAny<ClassifyTokenClientQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheGateway_LeavesTheRequestUnmarked_AndLetsItGoOn()
    {
        var context = TokenRequest();
        var reachedNext = false;

        await new ClientAssertionPresenceMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, _ => true)
            .InvokeAsync(context);

        reachedNext.Should().BeTrue("the upstream answers the request as it would have");
        ClientAssertionPresenceMiddleware.IsMarked(context).Should().BeFalse();
    }
}
