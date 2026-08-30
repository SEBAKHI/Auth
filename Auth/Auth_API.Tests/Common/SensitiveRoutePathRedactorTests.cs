using Auth_API.Common;
using Microsoft.AspNetCore.Http;

namespace Auth_API.Tests.Common;

/// <summary>
/// Guards the request-path redaction that keeps invitation tokens out of
/// <c>Logs/auth-api-*.log</c>. Production keeps ninety rolls of that file, so a
/// token written there is readable for far longer than the invitation lives.
/// </summary>
public class SensitiveRoutePathRedactorTests
{
    private static DefaultHttpContext ContextWithRouteValues(params (string Key, object? Value)[] routeValues)
    {
        var context = new DefaultHttpContext();
        foreach (var (key, value) in routeValues)
        {
            context.Request.RouteValues[key] = value;
        }
        return context;
    }

    [Fact]
    public void Redact_InvitationToken_ReplacesItWithTheParameterName()
    {
        const string token = "cHJldmlldy10b2tlbi10aGF0LWlzLWxvbmctZW5vdWdo";
        var context = ContextWithRouteValues(("token", token));

        var redacted = SensitiveRoutePathRedactor.Redact(context, $"/api/v1/Invitations/{token}");

        redacted.Should().Be("/api/v1/Invitations/{token}");
        redacted.Should().NotContain(token);
    }

    [Fact]
    public void Redact_TokenInTheMiddleOfAPath_StillReplacesIt()
    {
        const string token = "an-invitation-token-value";
        var context = ContextWithRouteValues(("token", token));

        SensitiveRoutePathRedactor.Redact(context, $"/api/v1/Invitations/{token}/register")
            .Should().Be("/api/v1/Invitations/{token}/register");
    }

    [Fact]
    public void Redact_NonSensitiveRouteValues_AreLeftAlone()
    {
        // The point of a narrow redaction: an access log without user and
        // organization ids answers no operational question worth asking.
        var userId = Guid.NewGuid().ToString();
        var context = ContextWithRouteValues(("id", userId));

        SensitiveRoutePathRedactor.Redact(context, $"/api/v1/Users/{userId}")
            .Should().Be($"/api/v1/Users/{userId}");
    }

    [Fact]
    public void Redact_MixedRoute_RedactsOnlyTheToken()
    {
        const string token = "secret-token";
        var orgId = Guid.NewGuid().ToString();
        var context = ContextWithRouteValues(("orgId", orgId), ("token", token));

        var redacted = SensitiveRoutePathRedactor.Redact(context, $"/api/v1/Organizations/{orgId}/invitations/{token}");

        redacted.Should().Contain(orgId);
        redacted.Should().NotContain(token);
    }

    [Fact]
    public void Redact_NoRouteValues_ReturnsThePathUnchanged()
    {
        // A 404, or anything handled before routing bound a value. Nothing was
        // bound, so nothing can be identified as a secret.
        var context = new DefaultHttpContext();

        SensitiveRoutePathRedactor.Redact(context, "/api/v1/does-not-exist")
            .Should().Be("/api/v1/does-not-exist");
    }

    [Fact]
    public void Redact_EmptyTokenValue_ReturnsThePathUnchanged()
    {
        // Replacing an empty string would splice the marker between every
        // character in the path.
        var context = ContextWithRouteValues(("token", string.Empty));

        SensitiveRoutePathRedactor.Redact(context, "/api/v1/Invitations/")
            .Should().Be("/api/v1/Invitations/");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Redact_MissingPath_IsReturnedAsGiven(string? requestPath)
    {
        var context = ContextWithRouteValues(("token", "whatever"));

        SensitiveRoutePathRedactor.Redact(context, requestPath!).Should().Be(requestPath);
    }
}
