using System.Security.Claims;
using Auth.Domain.Constants;
using Auth_API.Common.Authentication;
using Microsoft.AspNetCore.Http;

namespace Auth_API.Tests.Authentication;

/// <summary>
/// REVIEW-FIXES 152 F23: the reauthentication guard reads when the request's access token was
/// issued from the principal its scheme validated (<c>iat</c>, whole seconds), and fails closed on
/// anything else: outside a request, no claim, or a value that is not a time in seconds.
/// </summary>
public class RequestAccessTokenTests
{
    private static RequestAccessToken With(params Claim[] claims)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) };
        return new RequestAccessToken(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void IssuedAtUtc_ReadsTheValidatedTokensIat_AsUtc()
    {
        var issued = new DateTimeOffset(2026, 11, 10, 12, 0, 7, TimeSpan.Zero);

        var token = With(new Claim(JwtClaimNames.IssuedAt, issued.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

        token.IssuedAtUtc.Should().Be(issued.UtcDateTime);
        token.IssuedAtUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1731240007.5")]
    [InlineData("99999999999999")]
    public void IssuedAtUtc_AnythingButATimeInSeconds_IsNone(string? value)
    {
        var token = value is null ? With() : With(new Claim(JwtClaimNames.IssuedAt, value));

        token.IssuedAtUtc.Should().BeNull();
    }

    [Fact]
    public void IssuedAtUtc_OutsideARequest_IsNone()
    {
        new RequestAccessToken(new HttpContextAccessor()).IssuedAtUtc.Should().BeNull();
    }
}
