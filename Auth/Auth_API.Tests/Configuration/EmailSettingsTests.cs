using Auth.Application.Configuration;

namespace Auth_API.Tests.Configuration;

/// <summary>
/// Unit tests for EmailSettings URL construction. The password reset link is the
/// only way a user can reach the reset page, so its shape is worth pinning down.
/// </summary>
public class EmailSettingsTests
{
    private static EmailSettings WithBaseUrl(string baseUrl) => new() { FrontendBaseUrl = baseUrl };

    [Fact]
    public void BuildPasswordResetUrl_ProducesAbsoluteUrlCarryingTheToken()
    {
        var settings = WithBaseUrl("https://accounts.example.com");

        var url = settings.BuildPasswordResetUrl("plain-token");

        url.Should().Be("https://accounts.example.com/reset-password?token=plain-token");
    }

    [Fact]
    public void BuildPasswordResetUrl_DoesNotDoubleUpSlashes()
    {
        var settings = WithBaseUrl("https://accounts.example.com/");

        var url = settings.BuildPasswordResetUrl("plain-token");

        url.Should().Be("https://accounts.example.com/reset-password?token=plain-token");
    }

    [Fact]
    public void BuildPasswordResetUrl_EscapesTokenCharactersThatWouldBreakTheQueryString()
    {
        // Generated tokens are URL-safe base64, but escaping must not be dropped:
        // an unescaped '+' or '&' would silently truncate or corrupt the token.
        var settings = WithBaseUrl("https://accounts.example.com");

        var url = settings.BuildPasswordResetUrl("a+b/c=d&e");

        url.Should().Be("https://accounts.example.com/reset-password?token=a%2Bb%2Fc%3Dd%26e");
    }

    [Fact]
    public void BuildFrontendUrl_AppendsPathToConfiguredBase()
    {
        var settings = WithBaseUrl("https://accounts.example.com");

        settings.BuildFrontendUrl("/accept-invitation?token=abc")
            .Should().Be("https://accounts.example.com/accept-invitation?token=abc");
    }
}
