using Auth.Application.Configuration;
using Auth_API.Common.Middleware;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Auth_API.Tests.Middleware;

/// <summary>
/// Guards the exempt-path matching of <see cref="GatewayTokenValidationMiddleware"/>
/// against the database layer's array tombstones: an empty ExemptPaths entry
/// would prefix-match every request and silently disable gateway enforcement
/// for the whole API.
/// </summary>
public class GatewayExemptPathGuardTests
{
    private readonly Mock<ILogger<GatewayTokenValidationMiddleware>> _loggerMock = new();

    private GatewayTokenValidationMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, _loggerMock.Object);

    private static TestHelpers.TestOptions<GatewaySettings> Settings(params string[] exemptPaths)
        => TestHelpers.CreateOptions(new GatewaySettings
        {
            ValidationEnabled = true,
            ExpectedToken = "secret-token",
            TokenHeaderName = "X-Gateway-Token",
            ExemptPaths = exemptPaths
        });

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = path;
        return context;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_BlankExemptEntry_DoesNotExemptApiPaths(string blankEntry)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/v1/anything");

        await middleware.InvokeAsync(context, Settings(blankEntry));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_BlankEntryAlongsideRealPrefix_DoesNotWidenTheExemption()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/v1/users");

        await middleware.InvokeAsync(context, Settings("", "/health"));

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RealExemptPrefix_StillPassesThroughNextToBlankEntry()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/health");

        await middleware.InvokeAsync(context, Settings("", "/health"));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ExemptPrefix_MatchesSubPathsWithSegmentBoundary()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/health/live");

        await middleware.InvokeAsync(context, Settings("/health"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RejectedRequest_WritesProblemJson403()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateContext("/api/v1/anything");

        await middleware.InvokeAsync(context, Settings(""));

        context.Response.StatusCode.Should().Be(403);
        context.Response.ContentType.Should().Be("application/problem+json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("403");
        body.Should().Contain("/api/v1/anything");
    }
}
