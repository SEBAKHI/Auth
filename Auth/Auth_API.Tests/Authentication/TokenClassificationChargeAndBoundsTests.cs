using System.Text;
using Auth.Application.Features.Authentication.ClassifyTokenClient;
using Auth.Shared.Http;
using Auth_API.Common.Middleware;
using Auth_API.Common.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// The classification before the token endpoint's limiter (OI-178, review round of PR #81): what it
/// charges on an address when a verification does not end in a verified result, and which bodies
/// it reads at all.
/// </summary>
public class TokenClassificationChargeAndBoundsTests
{
    private const int PerAddress = 3;
    private const string Form =
        "grant_type=refresh_token&refresh_token=r" +
        "&client_assertion_type=urn%3Aietf%3Aparams%3Aoauth%3Aclient-assertion-type%3Ajwt-bearer&client_assertion=x";

    /// <summary>A request body that records whether anything read it.</summary>
    private sealed class WatchedBody(byte[] content) : MemoryStream(content)
    {
        public bool WasRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            return base.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            WasRead = true;
            return base.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private static DefaultHttpContext TokenRequest(string contentType, string body, long? declaredLength = -1)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ClassifiesTokenClientAttribute()), "token"));
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = contentType;
        context.Request.ContentLength = declaredLength == -1 ? bytes.Length : declaredLength;
        context.Request.Body = new WatchedBody(bytes);
        return context;
    }

    private static TokenClientClassificationMiddleware Classification(TokenAssertionFailureBudget failures) =>
        new(_ => Task.CompletedTask, failures, new TokenAssertionSightings(TimeProvider.System));

    // ===== F3: a verification that throws or is abandoned is charged =====

    public static TheoryData<Exception> VerificationFaults => new()
    {
        new InvalidOperationException("the key table could not be read"),
        new OperationCanceledException("the client went away during the key read"),
    };

    [Theory]
    [MemberData(nameof(VerificationFaults))]
    public async Task AVerificationThatThrows_IsChargedOnTheAddress_SoItsAllowanceRunsOut(Exception fault)
    {
        // Review finding (F3): only an error result was charged, so a request that made the key
        // read fail (or abandoned it) had keys read without spending its allowance.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ClassifyTokenClientQuery>(), It.IsAny<CancellationToken>())).ThrowsAsync(fault);
        using var failures = new TokenAssertionFailureBudget(() => 0, () => new FixedWindowBudget(PerAddress, 3600));
        var classification = Classification(failures);

        for (var i = 0; i < PerAddress; i++)
        {
            var act = () => classification.InvokeAsync(TokenRequest("application/x-www-form-urlencoded", Form), sender.Object);
            await act.Should().ThrowAsync<Exception>("the fault surfaces as before");
        }

        var past = TokenRequest("application/x-www-form-urlencoded", Form);
        await classification.InvokeAsync(past, sender.Object);

        TokenRequestClass.Of(past).Should().BeSameAs(TokenRequestClass.Refused);
        sender.Verify(s => s.Send(It.IsAny<ClassifyTokenClientQuery>(), It.IsAny<CancellationToken>()), Times.Exactly(PerAddress),
            "past the allowance nothing is verified");
    }

    // ===== F4: only the small url-encoded form the action accepts is read =====

    public static TheoryData<string, string, long?> FormsTheActionDoesNotTakeAhead => new()
    {
        { "multipart/form-data; boundary=b", "--b\r\nContent-Disposition: form-data; name=\"client_assertion\"\r\n\r\nx\r\n--b--\r\n", -1 },
        { "application/x-www-form-urlencoded", Form + "&pad=" + new string('a', ClientAssertionPresenceMiddleware.MaxFormBytes), -1 },
        { "application/x-www-form-urlencoded", Form, null },
    };

    [Theory]
    [MemberData(nameof(FormsTheActionDoesNotTakeAhead))]
    public async Task AFormThatIsNotASmallUrlEncodedOne_IsNotReadBeforeTheLimiter(string contentType, string body, long? declaredLength)
    {
        // Review finding (F4): the classification read any form, of any type and size, before the
        // limiter, where main read none. Multipart, oversized and chunked bodies now reach the
        // limiter unread, counted on their address, and are answered after it as before.
        var sender = new Mock<ISender>();
        using var failures = new TokenAssertionFailureBudget(() => 0, () => new FixedWindowBudget(PerAddress, 3600));
        var context = TokenRequest(contentType, body, declaredLength);

        await Classification(failures).InvokeAsync(context, sender.Object);

        ((WatchedBody)context.Request.Body).WasRead.Should().BeFalse();
        TokenRequestClass.Of(context).Should().BeSameAs(TokenRequestClass.ByAddress);
        sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ASmallUrlEncodedForm_IsStillClassified()
    {
        // The floor: the same request, of a declared length within the edge's bound, is read.
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<ClassifyTokenClientQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Auth.Domain.Errors.AuthErrors.InvalidClient);
        using var failures = new TokenAssertionFailureBudget(() => 0, () => new FixedWindowBudget(PerAddress, 3600));
        var context = TokenRequest("application/x-www-form-urlencoded; charset=utf-8", Form);

        await Classification(failures).InvokeAsync(context, sender.Object);

        ((WatchedBody)context.Request.Body).WasRead.Should().BeTrue();
        sender.Verify(s => s.Send(It.IsAny<ClassifyTokenClientQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
