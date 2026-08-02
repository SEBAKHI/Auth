using Auth.Application.Features.Authentication.Common;

namespace Auth_API.Tests.Common;

/// <summary>
/// The two halves of the device string are joined on the way into the session
/// row and taken apart again for device recognition. A drift between the two
/// would silently break recognition, so the round-trip is asserted directly.
/// </summary>
public class AuthenticationHelperTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0", "device-abc")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0) Chrome/120.0", null)]
    [InlineData(null, "device-abc")]
    [InlineData(null, null)]
    public void ParseDeviceInfo_InvertsBuildDeviceInfo(string? userAgent, string? deviceId)
    {
        var combined = AuthenticationHelper.BuildDeviceInfo(userAgent, deviceId);

        var (parsedAgent, parsedId) = AuthenticationHelper.ParseDeviceInfo(combined);

        parsedAgent.Should().Be(string.IsNullOrEmpty(userAgent) ? null : userAgent);
        parsedId.Should().Be(string.IsNullOrEmpty(deviceId) ? null : deviceId);
    }

    [Fact]
    public void ParseDeviceInfo_KeepsAUserAgentThatItselfContainsTheSeparatorWord()
    {
        // "DeviceId:" appearing inside a UA must not be mistaken for the join.
        var userAgent = "Mozilla/5.0 (compatible; DeviceId: not-really)";
        var combined = AuthenticationHelper.BuildDeviceInfo(userAgent, "real-id");

        var (parsedAgent, parsedId) = AuthenticationHelper.ParseDeviceInfo(combined);

        parsedAgent.Should().Be(userAgent);
        parsedId.Should().Be("real-id");
    }
}
