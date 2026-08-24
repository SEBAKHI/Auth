namespace Auth_API.Tests.Security;

/// <summary>
/// Guards the one place a bearer token may enter the API.
///
/// JwtBlacklistValidationMiddleware is the only per-request revocation check, and it reads
/// the token from the Authorization header alone — when that header is absent it calls the
/// next middleware and returns, so the request is never checked against the jti, sid or
/// user-revocation lists. Everything downstream is already authenticated by then, because
/// JwtBearer runs first.
///
/// That makes the intake path load-bearing: any second source of a token skips revocation
/// entirely. Program.cs used to accept one from the query string, commented as WebSocket
/// support for a solution that has no WebSocket, so a revoked, logged-out or locked-out
/// token still authenticated when passed as ?access_token=.
///
/// These tests fail if that source comes back, and fail if the coupling they rest on
/// changes — if the middleware is ever made source-agnostic, the second test is the one
/// that should be reconsidered first.
/// </summary>
public class BearerTokenIntakeConformanceTests
{
    [Fact]
    public void Program_DoesNotSupplyTheBearerTokenFromAnySourceOtherThanTheHeader()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "Auth_API", "Program.cs"));

        program.Should().NotContain(
            "context.Token =",
            "assigning JwtBearerEvents' token routes the request around JwtBlacklistValidationMiddleware, "
            + "which only inspects the Authorization header — a revoked token supplied any other way would authenticate");

        program.Should().NotContain(
            "Query[\"access_token\"]",
            "there is no WebSocket, SignalR hub or download endpoint in this solution that authenticates by query "
            + "string, and uploads are served by UseStaticFiles with no authentication at all");
    }

    [Fact]
    public void BlacklistMiddleware_StillReadsTheAuthorizationHeaderOnly()
    {
        var middleware = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "Auth_API", "Common", "Middleware", "JwtBlacklistValidationMiddleware.cs"));

        middleware.Should().Contain(
            "context.Request.Headers.Authorization",
            "the sibling test bans every other token source precisely because this middleware reads only this one");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Auth.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Auth.sln not found above the test output directory.");
    }
}
