using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Errors;
using Auth.Shared.Http;
using Auth.Shared.Http.ErrorContract;
using Auth_API.Common.Middleware;
using Auth_API.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Middleware;

#region SecurityHeadersMiddleware Tests

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WithoutEndpointPolicy_AddsBaselineHeaders()
    {
        var (context, responseFeature) = CreateContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);
        await responseFeature.FireOnStartingAsync();

        nextCalled.Should().BeTrue();
        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Be(
            "default-src 'self'; frame-ancestors 'none'");
        context.Response.Headers.XFrameOptions.ToString().Should().Be("DENY");
        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task InvokeAsync_WithEndpointPolicy_PreservesTheEndpointPolicy()
    {
        var (context, responseFeature) = CreateContext();
        const string documentPolicy =
            "default-src 'none'; style-src 'sha256-document'; frame-ancestors 'none'";
        var middleware = new SecurityHeadersMiddleware(nextContext =>
        {
            nextContext.Response.Headers.ContentSecurityPolicy = documentPolicy;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await responseFeature.FireOnStartingAsync();

        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Be(documentPolicy,
            "a gateway must not replace the policy set by the document-producing endpoint");
    }

    private static (DefaultHttpContext Context, CallbackResponseFeature ResponseFeature)
        CreateContext()
    {
        var responseFeature = new CallbackResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(responseFeature);
        return (new DefaultHttpContext(features), responseFeature);
    }

    /// <summary>
    /// Minimal response feature that exposes the server's OnStarting boundary.
    /// DefaultHttpContext does not execute those callbacks when isolated from a
    /// server, while this middleware's correctness is specifically about which
    /// header exists at that final boundary.
    /// </summary>
    private sealed class CallbackResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) =>
            _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task FireOnStartingAsync()
        {
            for (var index = _onStarting.Count - 1; index >= 0; index--)
            {
                var (callback, state) = _onStarting[index];
                await callback(state);
            }

            HasStarted = true;
        }
    }
}

#endregion

#region GatewayTokenValidationMiddleware Tests

public class GatewayTokenValidationMiddlewareTests
{
    private readonly Mock<ILogger<GatewayTokenValidationMiddleware>> _loggerMock = new();

    private GatewayTokenValidationMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new GatewayTokenValidationMiddleware(next, _loggerMock.Object);
    }

    private static TestHelpers.TestOptions<GatewaySettings> CreateSettings(
        bool validationEnabled = true,
        string expectedToken = "secret-token",
        string tokenHeaderName = "X-Gateway-Token",
        string[]? exemptPaths = null)
    {
        return TestHelpers.CreateOptions(new GatewaySettings
        {
            ValidationEnabled = validationEnabled,
            ExpectedToken = expectedToken,
            TokenHeaderName = tokenHeaderName,
            ExemptPaths = exemptPaths ?? new[] { "/health", "/.well-known/" }
        });
    }

    [Fact]
    public async Task InvokeAsync_ValidationDisabled_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";

        await middleware.InvokeAsync(context, CreateSettings(validationEnabled: false));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ExemptPath_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context, CreateSettings());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ExemptPathPrefix_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/.well-known/openid-configuration";

        await middleware.InvokeAsync(context, CreateSettings());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MissingToken_Returns403()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/users";

        await middleware.InvokeAsync(context, CreateSettings());

        context.Response.StatusCode.Should().Be(403);
        context.Items[ProblemItems.Code].Should().Be(AuthErrors.InvalidGatewayToken.Code);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_InvalidToken_Returns403()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/users";
        context.Request.Headers["X-Gateway-Token"] = "wrong-token";

        await middleware.InvokeAsync(context, CreateSettings(expectedToken: "secret-token"));

        context.Response.StatusCode.Should().Be(403);
        context.Items[ProblemItems.Code].Should().Be(AuthErrors.InvalidGatewayToken.Code);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_EmptyToken_Returns403()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Path = "/api/users";
        context.Request.Headers["X-Gateway-Token"] = "";

        await middleware.InvokeAsync(context, CreateSettings());

        context.Response.StatusCode.Should().Be(403);
        context.Items[ProblemItems.Code].Should().Be(AuthErrors.InvalidGatewayToken.Code);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ValidToken_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/users";
        context.Request.Headers["X-Gateway-Token"] = "secret-token";

        await middleware.InvokeAsync(context, CreateSettings(expectedToken: "secret-token"));

        nextCalled.Should().BeTrue();
    }
}

#endregion

#region JwtBlacklistValidationMiddleware Tests

public class JwtBlacklistValidationMiddlewareTests
{
    private readonly Mock<ILogger<JwtBlacklistValidationMiddleware>> _loggerMock = new();
    private readonly Mock<ITokenBlacklistService> _blacklistMock = new();

    private JwtBlacklistValidationMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new JwtBlacklistValidationMiddleware(next, _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_NoAuthHeader_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_NonBearerAuth_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_EmptyBearerToken_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_InvalidJwtFormat_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer not-a-jwt";

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_BlacklistedJti_Returns401()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        // Create a minimal valid JWT with a jti claim
        var jti = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var token = CreateMinimalJwt(jti, userId);
        context.Request.Headers.Authorization = $"Bearer {token}";

        _blacklistMock.Setup(b => b.IsTokenBlacklisted(jti)).Returns(true);

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        context.Response.StatusCode.Should().Be(401);
        context.Items[ProblemItems.Code].Should().Be(ChallengeReasonCodes.TokenRevoked);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_TokensRevokedForTheUser_Returns401WithTokenRevoked()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var userId = Guid.NewGuid();
        context.Request.Headers.Authorization = $"Bearer {CreateMinimalJwt(Guid.NewGuid().ToString(), userId)}";

        _blacklistMock.Setup(b => b.AreUserTokensBlacklisted(userId, It.IsAny<DateTime>())).Returns(true);

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        context.Response.StatusCode.Should().Be(401);
        context.Items[ProblemItems.Code].Should().Be(ChallengeReasonCodes.TokenRevoked);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }

    [Fact]
    public async Task InvokeAsync_RevokedSession_Returns401WithSessionRevoked()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        var sessionId = Guid.NewGuid().ToString();
        context.Request.Headers.Authorization =
            $"Bearer {CreateMinimalJwt(Guid.NewGuid().ToString(), Guid.NewGuid(), sessionId)}";

        _blacklistMock.Setup(b => b.IsSessionBlacklisted(sessionId)).Returns(true);

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
        context.Items[ProblemItems.Code].Should().Be(ChallengeReasonCodes.SessionRevoked);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer error=\"invalid_token\"");
    }

    [Fact]
    public async Task InvokeAsync_NonBlacklistedToken_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        var jti = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var token = CreateMinimalJwt(jti, userId);
        context.Request.Headers.Authorization = $"Bearer {token}";

        _blacklistMock.Setup(b => b.IsTokenBlacklisted(jti)).Returns(false);
        _blacklistMock.Setup(b => b.AreUserTokensBlacklisted(userId, It.IsAny<DateTime>())).Returns(false);

        await middleware.InvokeAsync(context, _blacklistMock.Object);

        nextCalled.Should().BeTrue();
    }

    /// <summary>
    /// Creates a minimal unsigned JWT with jti and sub claims (and sid, when given) for testing.
    /// Format: base64(header).base64(payload).signature
    /// </summary>
    private static string CreateMinimalJwt(string jti, Guid userId, string? sid = null)
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var session = sid is null ? string.Empty : $",\"sid\":\"{sid}\"";
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{{\"jti\":\"{jti}\",\"sub\":\"{userId}\",\"iat\":{iat}{session}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{header}.{payload}.";
    }
}

#endregion
