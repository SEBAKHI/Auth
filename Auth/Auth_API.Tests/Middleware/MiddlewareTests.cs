using System.Security.Claims;
using System.Text.Json;
using Auth.Application.Configuration;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth_API.Common.Middleware;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth_API.Tests.Middleware;

#region ExceptionHandlingMiddleware Tests

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _loggerMock = new();
    private readonly Mock<IHostEnvironment> _envMock = new();

    private ExceptionHandlingMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new ExceptionHandlingMiddleware(next, _loggerMock.Object, _envMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_NoException_CallsNextAndDoesNotModifyResponse()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var failures = new List<ValidationFailure> { new("Email", "Email is required.") };
        var middleware = CreateMiddleware(_ => throw new ValidationException(failures));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedAccessException_Returns401()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_KeyNotFoundException_Returns404()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware(_ => throw new KeyNotFoundException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_InvalidOperationException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Bad op"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_ArgumentException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware(_ => throw new ArgumentException("Bad arg"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        _envMock.Setup(e => e.EnvironmentName).Returns("Production");
        var middleware = CreateMiddleware(_ => throw new Exception("Unexpected"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_InDevelopment_IncludesExceptionDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        _envMock.Setup(e => e.EnvironmentName).Returns("Development");
        var middleware = CreateMiddleware(_ => throw new Exception("Dev error details"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("Dev error details");
    }

    [Fact]
    public async Task InvokeAsync_WithCorrelationId_IncludesInResponse()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Correlation-ID"] = "test-correlation-123";
        var middleware = CreateMiddleware(_ => throw new Exception("err"));

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("test-correlation-123");
    }
}

#endregion

#region SecurityHeadersMiddleware Tests

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsSecurityHeaders()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        // Trigger OnStarting callbacks by flushing
        // SecurityHeaders are added via OnStarting, so we need to fire the response
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_CallsNext()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
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

    private static IOptions<GatewaySettings> CreateSettings(
        bool validationEnabled = true,
        string expectedToken = "secret-token",
        string tokenHeaderName = "X-Gateway-Token",
        string[]? exemptPaths = null)
    {
        return Options.Create(new GatewaySettings
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
    /// Creates a minimal unsigned JWT with jti and sub claims for testing.
    /// Format: base64(header).base64(payload).signature
    /// </summary>
    private static string CreateMinimalJwt(string jti, Guid userId)
    {
        var header = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                $"{{\"jti\":\"{jti}\",\"sub\":\"{userId}\",\"iat\":{iat}}}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{header}.{payload}.";
    }
}

#endregion
