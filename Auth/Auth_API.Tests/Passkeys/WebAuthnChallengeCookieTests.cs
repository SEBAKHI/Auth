using Auth.Domain.Entities;
using Auth_API.Common.Passkeys;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Auth_API.Tests.Passkeys;

/// <summary>
/// S20 (PK6, ARR-0687): the challenge cookie carries a random handle — the challenge itself
/// stays server-side — under a <c>__Host-</c> name with <c>Path=/</c>, so no sibling host can
/// set one beside it for this host. A request that carries two is one where someone tried: it
/// is refused before anything is spent (the handler's half is in
/// <c>RegisterPasskeyCommandHandlerTests</c>).
/// </summary>
/// <remarks>
/// Deliberate breaks, each verified red: <c>Path</c> set to <c>/api/v1/auth/passkeys</c>; the
/// duplicate check dropped (the first cookie taken).
/// </remarks>
public class WebAuthnChallengeCookieTests
{
    [Fact]
    public void TheCookie_IsAHostCookie_OnPathRoot_HttpOnly_Secure_Strict_ForFiveMinutes()
    {
        var context = new DefaultHttpContext();

        WebAuthnChallengeCookie.Apply(context.Response, "handle-123");

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        cookie.Name.Value.Should().Be(WebAuthnChallengeCookie.Name).And.StartWith("__Host-");
        cookie.Value.Value.Should().Be("handle-123");
        cookie.Path.Value.Should().Be("/", "a __Host- cookie must have Path=/ and no Domain, or the browser drops it");
        cookie.Domain.HasValue.Should().BeFalse();
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        cookie.MaxAge.Should().Be(WebAuthnChallenge.Lifetime);
    }

    [Fact]
    public void Delete_ExpiresTheSameCookie_OnTheSamePath()
    {
        var context = new DefaultHttpContext();

        WebAuthnChallengeCookie.Delete(context.Response);

        var cookie = SetCookieHeaderValue.Parse(context.Response.Headers.SetCookie.ToString());
        cookie.Name.Value.Should().Be(WebAuthnChallengeCookie.Name);
        cookie.Path.Value.Should().Be("/");
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Apply_WritesTheHandle_WithThoseAttributes()
    {
        var context = new DefaultHttpContext();

        WebAuthnChallengeCookie.Apply(context.Response, "handle-123");

        var header = context.Response.Headers.SetCookie.ToString();
        header.Should().StartWith("__Host-auth_webauthn=handle-123;")
            .And.Contain("path=/;").And.Contain("secure").And.Contain("samesite=strict").And.Contain("httponly")
            .And.NotContain("domain=");
    }

    [Fact]
    public void Read_OneCookie_IsTheHandle()
    {
        WebAuthnChallengeCookie.Read(Request("__Host-auth_webauthn=abc; other=1")).Should().Be(("abc", false));
    }

    [Theory]
    [InlineData("__Host-auth_webauthn=abc; __Host-auth_webauthn=planted")]
    [InlineData("__Host-auth_webauthn=abc", "__Host-auth_webauthn=planted")]
    public void Read_TwoCookies_AreRefused_AndNeitherIsTaken(params string[] headers)
    {
        WebAuthnChallengeCookie.Read(Request(headers)).Should().Be(((string?)null, true));
    }

    [Theory]
    [InlineData("other=1")]
    [InlineData("__Host-auth_webauthn=")]
    [InlineData("auth_webauthn=abc")]
    public void Read_NoHandle_IsNone(string header)
    {
        WebAuthnChallengeCookie.Read(Request(header)).Should().Be(((string?)null, false));
    }

    private static HttpRequest Request(params string[] cookieHeaders)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookieHeaders;
        return context.Request;
    }
}
